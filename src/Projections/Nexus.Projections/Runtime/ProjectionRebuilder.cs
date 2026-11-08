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
public sealed record ProjectionRebuildSummary(
    string Projection,
    IReadOnlyDictionary<string, long> RowsPerTopic,
    int Topics,
    int Partitions)
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
///   <see cref="ProjectionPipeline"/> as the worker (batching, retries, DLQ, transient back-off), pausing each
///   partition as it reaches its snapshot watermark.</item>
///   <item>Flush and commit the offsets under the group id, so the worker continues exactly where the
///   rebuild stopped. A commit failure (e.g. a worker joined meanwhile) fails the rebuild.</item>
/// </list>
/// Cancellation (Ctrl+C) stops it without committing: run it again.
/// </summary>
public sealed partial class ProjectionRebuilder
{
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
        await EnsureGroupIsIdleAsync(admin, projection, groupId, timeout).ConfigureAwait(false);
        await DeleteGroupOffsetsAsync(admin, projection, groupId, timeout).ConfigureAwait(false);

        await TruncateAsync(projection, ct).ConfigureAwait(false);
        LogTruncated(_logger, projection.Name, projection.TableName);

        var (targets, topics) = await SnapshotWatermarksAsync(admin, projection, timeout).ConfigureAwait(false);
        LogSnapshot(_logger, projection.Name, topics, targets.Count);

        var rowsPerTopic = await ReplayAsync(projection, groupId, targets, ct).ConfigureAwait(false);
        var summary = new ProjectionRebuildSummary(projection.Name, rowsPerTopic, topics, targets.Count);
        foreach (var (topic, rows) in rowsPerTopic.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            LogTopicSummary(_logger, projection.Name, topic, rows);
        }

        LogDone(_logger, projection.Name, summary.TotalRows, topics);
        return summary;
    }

    private static async Task EnsureGroupIsIdleAsync(IAdminClient admin, IProjection projection, string groupId, TimeSpan timeout)
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
    }

    private static async Task DeleteGroupOffsetsAsync(IAdminClient admin, IProjection projection, string groupId, TimeSpan timeout)
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

    private async Task<IReadOnlyDictionary<string, long>> ReplayAsync(
        IProjection projection, string groupId, Dictionary<TopicPartition, long> targets, CancellationToken ct)
    {
        if (targets.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        _runtime.Health.Register(groupId, ProjectionCatalog.SubscriptionLabel(projection));
        var deserializer = new AvroDeserializer<GenericRecord>(_runtime.SchemaRegistry);
        using var consumer = new ConsumerBuilder<string?, byte[]?>(_runtime.CreateConsumerConfig(projection, enablePartitionEof: true)).Build();
        try
        {
            var pipeline = new ProjectionPipeline(projection, _runtime, consumer, deserializer, commitFailureIsFatal: true);
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
                    MarkDoneIfReached(consumer, pipeline, targets, remaining, result.TopicPartition);
                }

                if (pipeline.FlushDue)
                {
                    await pipeline.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            await pipeline.FlushAsync(ct).ConfigureAwait(false);
            return new Dictionary<string, long>(pipeline.RowsInsertedPerTopic, StringComparer.Ordinal);
        }
        catch (KafkaException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProjectionRebuildException(
                $"Rebuild of {projection.Name} failed on Kafka ({ex.Error.Code}: {ex.Error.Reason}); nothing more was committed. Run it again.", ex);
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>A partition is done once the consumer's position reached its snapshot watermark; it is then paused for good.</summary>
    private static void MarkDoneIfReached(
        IConsumer<string?, byte[]?> consumer,
        ProjectionPipeline pipeline,
        Dictionary<TopicPartition, long> targets,
        HashSet<TopicPartition> remaining,
        TopicPartition tp)
    {
        if (!remaining.Contains(tp))
        {
            return;
        }

        var position = consumer.Position(tp);
        if (position.Value >= 0 && position.Value >= targets[tp])
        {
            remaining.Remove(tp);
            pipeline.HeldPartitions.Add(tp);
            consumer.Pause([tp]);
        }
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuild {Projection}: truncated {Table}, consumer group offsets deleted")]
    private static partial void LogTruncated(ILogger logger, string projection, string table);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuild {Projection}: {Topics} matching topic(s), {Partitions} non-empty partition(s) to replay")]
    private static partial void LogSnapshot(ILogger logger, string projection, int topics, int partitions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuild {Projection}: {Topic} → {Rows} row(s)")]
    private static partial void LogTopicSummary(ILogger logger, string projection, string topic, long rows);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuild {Projection} done: {Rows} row(s) from {Topics} topic(s); offsets committed")]
    private static partial void LogDone(ILogger logger, string projection, long rows, int topics);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rebuild {Projection}: poll failed with {ErrorCode} ({Reason}); retrying")]
    private static partial void LogPollError(ILogger logger, string projection, ErrorCode errorCode, string reason);
}
