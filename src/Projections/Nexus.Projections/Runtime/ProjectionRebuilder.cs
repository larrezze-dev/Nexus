using Avro.Generic;
using ClickHouse.Client.Utility;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.Logging;
using Nexus.Projections.Abstractions;
using Nexus.Projections.Migrations;

namespace Nexus.Projections.Runtime;

/// <summary>What a successful rebuild did.</summary>
/// <param name="PoisonRecordsSkipped">
/// Records that exhausted their attempts during the replay. Not re-published to the DLQ (decision D2: the
/// worker already dead-lettered them); logged by topic/partition/offset.
/// </param>
public sealed record ProjectionRebuildSummary(
    string Projection,
    IReadOnlyDictionary<string, long> RowsPerTopic,
    int Topics,
    int Partitions,
    long PoisonRecordsSkipped)
{
    public long TotalRows => RowsPerTopic.Values.Sum();
}

/// <summary>The rebuild was refused or could not finish; the process exits non-zero.</summary>
public sealed class ProjectionRebuildException : Exception
{
    public ProjectionRebuildException()
    {
    }

    public ProjectionRebuildException(string message)
        : base(message)
    {
    }

    public ProjectionRebuildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// <c>nexus-projections rebuild &lt;projection&gt;</c> (conventions-events.md §8: "borra la tabla, resetea el
/// offset del grupo y relee el topic desde el inicio"):
/// <list type="number">
///   <item>Apply the ClickHouse migrations (as the worker does at startup).</item>
///   <item>Describe group <c>projections.&lt;name&gt;</c>: any active member → refuse ("stop nexus-projections
///   before rebuilding &lt;name&gt;").</item>
///   <item>Delete the group's committed offsets (<c>DeleteGroups</c>; a group that never committed is fine).
///   Done BEFORE the truncate on purpose: Kafka refuses to delete a non-empty group, so a worker that
///   joined since the describe makes the rebuild stop here, with the table still intact.</item>
///   <item><c>TRUNCATE TABLE</c> the projection's table.</item>
///   <item>Snapshot the high watermark of every partition of every topic currently matching the subscription
///   (a regex is expanded in .NET over the cluster metadata, <see cref="ProjectionTopicMatcher"/>; topics
///   or partitions with no message count as done).</item>
///   <item>Consume them from the beginning by manual assignment (no group membership) through the SAME
///   <see cref="ProjectionPipeline"/> as the worker (batching, retries, transient back-off) in
///   <see cref="ProjectionPipelineMode.Rebuild"/>: a poison record is counted and skipped, never re-published
///   to the DLQ (decision D2). Each partition is paused once its position reaches the snapshot watermark,
///   or when an end-of-partition event at/after it arrives (transaction control markers can keep the last
///   offsets unreachable), or when its topic was deleted meanwhile — so the replay always ends.</item>
///   <item>Every flushed batch commits its offsets under the group id (as the worker does); the last flush
///   hands over to the worker, which continues exactly where the rebuild stopped. A commit failure (e.g. a
///   worker joined meanwhile) fails the rebuild.</item>
/// </list>
/// Transient Kafka admin errors (coordinator loading or moving, timeouts, broker down) are retried a few
/// times with a short back-off; anything else stops the rebuild with the failed request in the message.
/// <para>
/// <b>Cancellation</b> (Ctrl+C) stops it: the batch in flight is neither inserted nor committed, but the
/// batches already flushed stay inserted AND committed, so table and offsets agree and the worker would
/// continue from there. Running <c>rebuild</c> again starts over.
/// </para>
/// </summary>
public sealed partial class ProjectionRebuilder
{
    private const int AdminAttempts = 5;
    private static readonly TimeSpan AdminInitialBackoff = TimeSpan.FromMilliseconds(500);

    private static readonly HashSet<ErrorCode> TransientAdminErrors =
    [
        ErrorCode.GroupLoadInProgress,
        ErrorCode.GroupCoordinatorNotAvailable,
        ErrorCode.NotCoordinatorForGroup,
        ErrorCode.RequestTimedOut,
        ErrorCode.LeaderNotAvailable,
        ErrorCode.NotLeaderForPartition,
        ErrorCode.NetworkException,
        ErrorCode.BrokerNotAvailable,
        ErrorCode.Local_TimedOut,
        ErrorCode.Local_Transport,
        ErrorCode.Local_AllBrokersDown,
    ];

    private static readonly HashSet<ErrorCode> MissingPartitionErrors =
    [
        ErrorCode.UnknownTopicOrPart,
        ErrorCode.Local_UnknownTopic,
        ErrorCode.Local_UnknownPartition,
    ];

    private readonly ProjectionCatalog _catalog;
    private readonly ProjectionRuntime _runtime;
    private readonly ClickHouseMigrator _migrator;
    private readonly ClickHouseConnectionProvider _clickHouse;
    private readonly ILogger<ProjectionRebuilder> _logger;

    public ProjectionRebuilder(
        ProjectionCatalog catalog,
        ProjectionRuntime runtime,
        ClickHouseMigrator migrator,
        ClickHouseConnectionProvider clickHouse,
        ILogger<ProjectionRebuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(migrator);
        ArgumentNullException.ThrowIfNull(clickHouse);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _runtime = runtime;
        _migrator = migrator;
        _clickHouse = clickHouse;
        _logger = logger;
    }

    /// <exception cref="ProjectionRebuildException">Refused (active members) or failed.</exception>
    public async Task<ProjectionRebuildSummary> RunAsync(string name, CancellationToken ct)
    {
        var projection = _catalog.Find(name)
            ?? throw new ProjectionRebuildException(
                $"Unknown projection '{name}'. Known projections: {string.Join(", ", _catalog.Names)}.");
        var groupId = ProjectionCatalog.GroupId(projection);
        var timeout = _runtime.Options.AdminRequestTimeout;

        await _migrator.ApplyAsync(ct).ConfigureAwait(false);

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _runtime.Kafka.BootstrapServers }).Build();
        await AdminAsync("describe consumer group", projection, () => EnsureGroupIsIdleAsync(admin, projection, groupId, timeout), ct).ConfigureAwait(false);
        await AdminAsync("delete consumer group offsets", projection, () => DeleteGroupOffsetsAsync(admin, projection, groupId, timeout), ct).ConfigureAwait(false);

        await TruncateAsync(projection, ct).ConfigureAwait(false);
        LogTruncated(_logger, projection.Name, projection.TableName);

        var (targets, topics) = await AdminAsync("snapshot high watermarks", projection, () => SnapshotWatermarksAsync(admin, projection, timeout), ct).ConfigureAwait(false);
        LogSnapshot(_logger, projection.Name, topics, targets.Count);

        var (rowsPerTopic, poison) = await ReplayAsync(admin, projection, groupId, targets, ct).ConfigureAwait(false);
        var summary = new ProjectionRebuildSummary(projection.Name, rowsPerTopic, topics, targets.Count, poison);
        foreach (var (topic, rows) in rowsPerTopic.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            LogTopicSummary(_logger, projection.Name, topic, rows);
        }

        var totalRows = summary.TotalRows;
        if (targets.Count == 0)
        {
            LogNothingToReplay(_logger, projection.Name, topics);
        }
        else
        {
            LogDone(_logger, projection.Name, totalRows, topics, poison);
        }

        return summary;
    }

    private static async Task<bool> EnsureGroupIsIdleAsync(IAdminClient admin, IProjection projection, string groupId, TimeSpan timeout)
    {
        var described = await admin.DescribeConsumerGroupsAsync(
            [groupId], new DescribeConsumerGroupsOptions { RequestTimeout = timeout }).ConfigureAwait(false);
        var members = described.ConsumerGroupDescriptions
            .Where(d => d.GroupId == groupId)
            .Sum(d => d.Members?.Count ?? 0);
        if (members > 0)
        {
            throw new ProjectionRebuildException(
                $"Consumer group {groupId} has {members} active member(s): stop nexus-projections before rebuilding {projection.Name}.");
        }

        return true;
    }

    private static async Task<bool> DeleteGroupOffsetsAsync(IAdminClient admin, IProjection projection, string groupId, TimeSpan timeout)
    {
        try
        {
            await admin.DeleteGroupsAsync([groupId], new DeleteGroupsOptions { RequestTimeout = timeout }).ConfigureAwait(false);
        }
        catch (DeleteGroupsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.GroupIdNotFound or ErrorCode.NoError))
        {
            // Nothing committed yet: nothing to reset.
        }
        catch (DeleteGroupsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.NonEmptyGroup))
        {
            throw new ProjectionRebuildException(
                $"Consumer group {groupId} became active: stop nexus-projections before rebuilding {projection.Name}.", ex);
        }

        return true;
    }

    private async Task TruncateAsync(IProjection projection, CancellationToken ct)
    {
        using var connection = _clickHouse.Create();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        // Identifier interpolation (no parameter binds a table name): TableName is validated by
        // ProjectionCatalog as <db>.<table> with [A-Za-z0-9_] parts and comes from code.
        await connection.ExecuteStatementAsync($"TRUNCATE TABLE {projection.TableName}").WaitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<(Dictionary<TopicPartition, long> Targets, int Topics)> SnapshotWatermarksAsync(
        IAdminClient admin, IProjection projection, TimeSpan timeout)
    {
        var matcher = new ProjectionTopicMatcher(projection.Subscription);
        var metadata = admin.GetMetadata(timeout);
        var matching = metadata.Topics.Where(t => !t.Error.IsError && matcher.Matches(t.Topic)).ToList();
        var partitions = matching.SelectMany(t => t.Partitions.Select(p => new TopicPartition(t.Topic, p.PartitionId))).ToList();
        var targets = new Dictionary<TopicPartition, long>();
        if (partitions.Count == 0)
        {
            return (targets, matching.Count);
        }

        var latest = await ListOffsetsAsync(admin, partitions, OffsetSpec.Latest(), timeout).ConfigureAwait(false);
        var earliest = await ListOffsetsAsync(admin, partitions, OffsetSpec.Earliest(), timeout).ConfigureAwait(false);
        foreach (var tp in partitions)
        {
            if (latest[tp] > earliest[tp])
            {
                targets[tp] = latest[tp]; // empty partitions are done already
            }
        }

        return (targets, matching.Count);
    }

    private async Task<(IReadOnlyDictionary<string, long> Rows, long Poison)> ReplayAsync(
        IAdminClient admin, IProjection projection, string groupId, Dictionary<TopicPartition, long> targets, CancellationToken ct)
    {
        if (targets.Count == 0)
        {
            return (new Dictionary<string, long>(StringComparer.Ordinal), 0);
        }

        _runtime.Health.Register(groupId, ProjectionCatalog.SubscriptionLabel(projection));
        var deserializer = new AvroDeserializer<GenericRecord>(_runtime.SchemaRegistry);
        using var consumer = new ConsumerBuilder<string?, byte[]?>(_runtime.CreateConsumerConfig(projection, enablePartitionEof: true)).Build();
        try
        {
            var pipeline = new ProjectionPipeline(projection, _runtime, consumer, deserializer, ProjectionPipelineMode.Rebuild);
            consumer.Assign(targets.Keys.Select(tp => new TopicPartitionOffset(tp, Offset.Beginning)));
            var remaining = targets.Keys.ToHashSet();
            var pollBackoff = TimeSpan.FromSeconds(1);

            while (remaining.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                ConsumeResult<string?, byte[]?>? result;
                try
                {
                    result = consumer.Consume(pipeline.TimeUntilFlush(ProjectionConsumer.PollTimeout));
                    pollBackoff = TimeSpan.FromSeconds(1);
                }
                catch (ConsumeException e) when (!e.Error.IsFatal)
                {
                    LogPollError(_logger, projection.Name, e.Error.Code, e.Error.Reason);
                    if (MissingPartitionErrors.Contains(e.Error.Code))
                    {
                        CompleteDeletedPartitions(admin, consumer, pipeline, remaining, projection.Name);
                    }

                    await Task.Delay(pollBackoff, _runtime.TimeProvider, ct).ConfigureAwait(false);
                    pollBackoff = ProjectionRuntime.NextBackoff(pollBackoff, TimeSpan.FromSeconds(5));
                    continue;
                }

                if (result is { IsPartitionEOF: false, Message: not null })
                {
                    await pipeline.ProcessAsync(result, ct).ConfigureAwait(false);
                }

                if (result is not null)
                {
                    MarkDoneIfReached(consumer, pipeline, targets, remaining, result);
                }

                if (pipeline.FlushDue)
                {
                    await pipeline.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            await pipeline.FlushAsync(ct).ConfigureAwait(false);
            return (new Dictionary<string, long>(pipeline.RowsInsertedPerTopic, StringComparer.Ordinal), pipeline.PoisonRecordsSkipped);
        }
        catch (KafkaException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProjectionRebuildException(
                $"Rebuild of {projection.Name} failed on Kafka ({ex.Error.Code}: {ex.Error.Reason}). Batches flushed before the failure stay committed; run rebuild again.", ex);
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>
    /// A partition is done once the consumer's position reached its snapshot watermark, or an
    /// end-of-partition event at/after the watermark arrived (control markers of transactional producers
    /// occupy offsets no record is ever returned for). It is then paused for good.
    /// </summary>
    private static void MarkDoneIfReached(
        IConsumer<string?, byte[]?> consumer,
        ProjectionPipeline pipeline,
        Dictionary<TopicPartition, long> targets,
        HashSet<TopicPartition> remaining,
        ConsumeResult<string?, byte[]?> result)
    {
        var tp = result.TopicPartition;
        if (!remaining.Contains(tp))
        {
            return;
        }

        var position = consumer.Position(tp);
        var reached = (position.Value >= 0 && position.Value >= targets[tp])
            || (result.IsPartitionEOF && result.Offset.Value >= targets[tp]);
        if (reached)
        {
            Complete(consumer, pipeline, remaining, tp);
        }
    }

    /// <summary>A topic (or partition) deleted since the snapshot can never reach its watermark: mark it done.</summary>
    private void CompleteDeletedPartitions(
        IAdminClient admin,
        IConsumer<string?, byte[]?> consumer,
        ProjectionPipeline pipeline,
        HashSet<TopicPartition> remaining,
        string projection)
    {
        Metadata metadata;
        try
        {
            metadata = admin.GetMetadata(_runtime.Options.AdminRequestTimeout);
        }
        catch (KafkaException)
        {
            return; // broker unreachable: the poll error path backs off and tries again
        }

        var existing = metadata.Topics
            .Where(t => !t.Error.IsError)
            .SelectMany(t => t.Partitions.Select(p => new TopicPartition(t.Topic, p.PartitionId)))
            .ToHashSet();
        foreach (var tp in remaining.Where(tp => !existing.Contains(tp)).ToList())
        {
            LogPartitionGone(_logger, projection, tp.Topic, tp.Partition.Value);
            Complete(consumer, pipeline, remaining, tp);
        }
    }

    private static void Complete(IConsumer<string?, byte[]?> consumer, ProjectionPipeline pipeline, HashSet<TopicPartition> remaining, TopicPartition tp)
    {
        remaining.Remove(tp);
        pipeline.HeldPartitions.Add(tp);
        consumer.Pause([tp]);
    }

    /// <summary>
    /// Runs one admin step, retrying transient Kafka errors (coordinator loading/moving, timeouts, broker
    /// down) up to <see cref="AdminAttempts"/> times with a doubling back-off from 500 ms. Any other Kafka
    /// error, or the last transient one, becomes a <see cref="ProjectionRebuildException"/> naming the step.
    /// </summary>
    private async Task<T> AdminAsync<T>(string operation, IProjection projection, Func<Task<T>> step, CancellationToken ct)
    {
        var backoff = AdminInitialBackoff;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await step().ConfigureAwait(false);
            }
            catch (KafkaException ex) when (attempt < AdminAttempts && IsTransientAdminError(ex))
            {
                LogAdminRetry(_logger, projection.Name, operation, attempt, ErrorSummary(ex), backoff);
                await Task.Delay(backoff, _runtime.TimeProvider, ct).ConfigureAwait(false);
                backoff *= 2;
            }
            catch (KafkaException ex)
            {
                throw new ProjectionRebuildException(
                    $"Rebuild of {projection.Name}: Kafka admin request '{operation}' failed ({ErrorSummary(ex)}).", ex);
            }
        }
    }

    /// <summary>
    /// True when every meaningful error code of <paramref name="exception"/> is transient (coordinator
    /// loading or moving, timeouts, broker down). For the typed admin exceptions only the PER-RESULT codes
    /// count: their top-level code is always <see cref="ErrorCode.Local_Partial"/> ("some results failed"),
    /// which says nothing about why. <c>Local_Partial</c> and <c>NoError</c> are never counted.
    /// </summary>
    internal static bool IsTransientAdminError(KafkaException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var codes = ErrorCodes(exception).ToList();
        return codes.Count > 0 && codes.All(TransientAdminErrors.Contains);
    }

    /// <summary>The meaningful error codes of <paramref name="exception"/> (never <c>NoError</c> nor <c>Local_Partial</c>).</summary>
    private static IEnumerable<ErrorCode> ErrorCodes(KafkaException exception)
    {
        IEnumerable<ErrorCode> codes = exception switch
        {
            DescribeConsumerGroupsException d => d.Results.ConsumerGroupDescriptions.Select(r => r.Error.Code),
            DeleteGroupsException d => d.Results.Select(r => r.Error.Code),
            ListOffsetsException l => l.Result.ResultInfos.Select(r => r.TopicPartitionOffsetError.Error.Code),
            _ => [exception.Error.Code],
        };
        return codes.Where(c => c is not ErrorCode.NoError and not ErrorCode.Local_Partial).Distinct();
    }

    private static string ErrorSummary(KafkaException exception) =>
        string.Join(", ", ErrorCodes(exception)) is { Length: > 0 } codes
            ? $"{codes}: {exception.Error.Reason}"
            : exception.Error.Reason;

    private static async Task<Dictionary<TopicPartition, long>> ListOffsetsAsync(
        IAdminClient admin, List<TopicPartition> partitions, OffsetSpec spec, TimeSpan timeout)
    {
        var result = await admin.ListOffsetsAsync(
            partitions.Select(tp => new TopicPartitionOffsetSpec { TopicPartition = tp, OffsetSpec = spec }),
            new ListOffsetsOptions { RequestTimeout = timeout }).ConfigureAwait(false);
        return result.ResultInfos.ToDictionary(
            r => r.TopicPartitionOffsetError.TopicPartition,
            r => r.TopicPartitionOffsetError.Offset.Value);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuild {Projection}: consumer group offsets deleted, {Table} truncated")]
    private static partial void LogTruncated(ILogger logger, string projection, string table);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuild {Projection}: {Topics} matching topic(s), {Partitions} non-empty partition(s) to replay")]
    private static partial void LogSnapshot(ILogger logger, string projection, int topics, int partitions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuild {Projection}: {Topic} → {Rows} row(s)")]
    private static partial void LogTopicSummary(ILogger logger, string projection, string topic, long rows);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Rebuild {Projection} done: {Rows} row(s) from {Topics} topic(s), {Poison} poison record(s) skipped (not re-published to the DLQ); offsets committed up to the snapshot")]
    private static partial void LogDone(ILogger logger, string projection, long rows, int topics, long poison);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Rebuild {Projection} done: {Topics} matching topic(s) but no message to replay; table empty, no offset committed (the worker starts from the earliest offsets)")]
    private static partial void LogNothingToReplay(ILogger logger, string projection, int topics);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rebuild {Projection}: poll failed with {ErrorCode} ({Reason}); retrying")]
    private static partial void LogPollError(ILogger logger, string projection, ErrorCode errorCode, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rebuild {Projection}: {Topic}[{Partition}] no longer exists; counted as done")]
    private static partial void LogPartitionGone(ILogger logger, string projection, string topic, int partition);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rebuild {Projection}: Kafka admin request '{Operation}' failed (attempt {Attempt}: {Error}); retrying in {Backoff}")]
    private static partial void LogAdminRetry(ILogger logger, string projection, string operation, int attempt, string error, TimeSpan backoff);
}
