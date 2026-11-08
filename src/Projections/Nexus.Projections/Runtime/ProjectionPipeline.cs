using Avro.Generic;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Runtime;

/// <summary>
/// The per-projection processing pipeline, shared by the worker loop (<see cref="ProjectionConsumer"/>) and
/// <c>rebuild</c> (<see cref="ProjectionRebuilder"/>) so both follow exactly the same rules:
/// <list type="number">
///   <item><b>Record → rows.</b> Each record is read as RAW bytes; per attempt: <see cref="EventEnvelope.FromHeaders"/>
///   → Avro decode as <see cref="GenericRecord"/> against the Schema Registry (a null value = tombstone =
///   null payload) → <see cref="IProjection.Map"/>. 4 attempts with back-off (<see cref="RetryPolicy"/>,
///   conventions-events.md §7); if all fail, the ORIGINAL record goes to <c>{topic}.DLQ</c> with the
///   <c>nexus-dlq-*</c> headers (consumer = the group id) and counts as done (zero rows).</item>
///   <item><b>Batching.</b> Rows accumulate until <see cref="ProjectionOptions.MaxBatchRows"/> or
///   <see cref="ProjectionOptions.FlushInterval"/> after the batch's first record. A flush is ONE
///   ClickHouse insert; only after it succeeds are the offsets committed (highest offset + 1 per
///   partition of the flushed records). Every record of the batch — with rows, skipped on purpose, or
///   dead-lettered — is covered by that commit.</item>
///   <item><b>Transient failures</b> (ClickHouse insert, Schema Registry unreachable) are never dead-lettered
///   and never committed (Luciano's 0.7 decision "transient ≠ FAILED"): the batch is kept, every assigned
///   partition is paused, the health registry reports <see cref="ConsumerLoopState.DependencyUnavailable"/>
///   (Unhealthy) and the operation is retried with exponential back-off (<see cref="ProjectionOptions.TransientInitialBackoff"/>
///   up to <see cref="ProjectionOptions.TransientMaxBackoff"/>) while the paused consumer keeps being
///   polled (group membership, rebalances). When it works again, the partitions resume.</item>
///   <item><b>DLQ unavailable</b>: same pause/back-off/keep-alive, reported as <see cref="ConsumerLoopState.DlqUnavailable"/>,
///   as <see cref="NexusConsumer{TEvent}"/> does; the record is never skipped.</item>
///   <item><b>Rebalance.</b> When partitions are revoked or lost, the buffered records of those partitions
///   are DROPPED without commit (<see cref="OnPartitionsRevoked"/>): their position goes back to the last
///   committed offset, so whoever owns them next re-reads them (at-least-once; re-inserted rows collapse
///   in the <c>ReplacingMergeTree</c>). A record whose partition was revoked while the pipeline waited on
///   a transient failure or on the DLQ is abandoned for the same reason. No offset whose row is not
///   inserted is ever committed.</item>
///   <item><b>Cancellation</b> never flushes, commits nor dead-letters: the buffer is lost and redelivered.</item>
/// </list>
/// Not thread-safe: it lives on its consumer's loop, and the rebalance callbacks run inside
/// <c>Consume</c> on that same flow.
/// </summary>
internal sealed partial class ProjectionPipeline
{
    private static readonly TimeSpan KeepAlivePollTimeout = TimeSpan.FromMilliseconds(100);

    private readonly IProjection _projection;
    private readonly ProjectionRuntime _runtime;
    private readonly IConsumer<string?, byte[]?> _consumer;
    private readonly Confluent.Kafka.IAsyncDeserializer<GenericRecord> _deserializer;
    private readonly RetryPolicy _retry;
    private readonly ILogger _logger;
    private readonly bool _commitFailureIsFatal;
    private readonly List<BufferedRecord> _buffer = [];
    private readonly Dictionary<TopicPartition, long> _generations = [];
    private readonly Dictionary<string, long> _rowsPerTopic = new(StringComparer.Ordinal);
    private readonly HashSet<TopicPartition> _held = [];
    private int _bufferedRows;
    private long _batchStartedAt;
    private DateTime _batchIngestedAt;
    private bool _paused;

    /// <param name="commitFailureIsFatal">
    /// Worker: false (a failed commit after a successful insert only means a redelivery, as in
    /// <see cref="NexusConsumer{TEvent}"/>). Rebuild: true (its commit is the hand-over to the worker).
    /// </param>
    public ProjectionPipeline(
        IProjection projection,
        ProjectionRuntime runtime,
        IConsumer<string?, byte[]?> consumer,
        Confluent.Kafka.IAsyncDeserializer<GenericRecord> deserializer,
        bool commitFailureIsFatal)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(deserializer);
        _projection = projection;
        _runtime = runtime;
        _consumer = consumer;
        _deserializer = deserializer;
        _commitFailureIsFatal = commitFailureIsFatal;
        _retry = new RetryPolicy(runtime.RetryOptions, runtime.TimeProvider);
        _logger = runtime.LoggerFactory.CreateLogger<ProjectionPipeline>();
        GroupId = ProjectionCatalog.GroupId(projection);
    }

    public string GroupId { get; }

    public int BufferedRecords => _buffer.Count;

    public int BufferedRows => _bufferedRows;

    /// <summary>Rows inserted so far, per topic (the rebuild summary).</summary>
    public IReadOnlyDictionary<string, long> RowsInsertedPerTopic => _rowsPerTopic;

    /// <summary>
    /// Partitions the pipeline must never resume after an outage (the rebuild pauses the partitions that
    /// reached their watermark).
    /// </summary>
    public ISet<TopicPartition> HeldPartitions => _held;

    /// <summary>A flush is due: rows reached the batch size, or the batch is older than the flush interval.</summary>
    public bool FlushDue =>
        _buffer.Count > 0
        && (_bufferedRows >= _runtime.Options.MaxBatchRows
            || _runtime.TimeProvider.GetElapsedTime(_batchStartedAt) >= _runtime.Options.FlushInterval);

    /// <summary>How long the loop may block in a poll before the current batch is due (at most <paramref name="max"/>).</summary>
    public TimeSpan TimeUntilFlush(TimeSpan max)
    {
        if (_buffer.Count == 0)
        {
            return max;
        }

        var left = _runtime.Options.FlushInterval - _runtime.TimeProvider.GetElapsedTime(_batchStartedAt);
        return left <= TimeSpan.Zero ? TimeSpan.Zero : left < max ? left : max;
    }

    /// <summary>
    /// Rebalance callback (revoked or lost): drops the buffered, uncommitted records of
    /// <paramref name="partitions"/>. Their next owner — possibly this consumer again — restarts from the
    /// last committed offset, so nothing is lost and nothing un-inserted is ever committed.
    /// </summary>
    public void OnPartitionsRevoked(IEnumerable<TopicPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        var revoked = partitions.ToHashSet();
        if (revoked.Count == 0)
        {
            return;
        }

        foreach (var tp in revoked)
        {
            _generations[tp] = Generation(tp) + 1;
        }

        var dropped = _buffer.RemoveAll(b => revoked.Contains(b.TopicPartition));
        if (dropped > 0)
        {
            _bufferedRows = _buffer.Sum(b => b.Rows.Count);
            LogDroppedOnRevoke(_logger, GroupId, dropped, revoked.Count);
        }
    }

    /// <summary>
    /// Turns one record into buffered rows (or a dead-lettered / skipped record). Returns when the record
    /// is accounted for in the batch, or abandoned because its partition was revoked meanwhile. Throws only
    /// on cancellation.
    /// </summary>
    public async Task ProcessAsync(ConsumeResult<string?, byte[]?> result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_buffer.Count == 0)
        {
            StartBatch();
        }

        var tp = result.TopicPartition;
        var generation = Generation(tp);

        if (ProjectionTopicMatcher.IsDlq(result.Topic))
        {
            // Never project a dead-letter topic (ficha 0.8), even if a pattern lets one through: skip, commit.
            LogDlqTopicSkipped(_logger, GroupId, result.Topic);
            Buffer(result, null, []);
            return;
        }

        var backoff = _runtime.Options.TransientInitialBackoff;
        while (true)
        {
            var outcome = await MapWithRetryAsync(result, ct).ConfigureAwait(false);

            if (outcome.Transient is { } transient)
            {
                _runtime.Metrics.Error(_projection.Name, ProjectionMetrics.ErrorKindSchemaRegistry);
                LogSchemaRegistryUnavailable(_logger, transient, GroupId, result.Topic, result.Partition.Value, result.Offset.Value, backoff);
                await EnterOutageAndWaitAsync(ConsumerLoopState.DependencyUnavailable, transient, backoff, ct).ConfigureAwait(false);
                backoff = ProjectionRuntime.NextBackoff(backoff, _runtime.Options.TransientMaxBackoff);
                if (Generation(tp) != generation)
                {
                    Abandon(result);
                    return;
                }

                continue;
            }

            if (outcome.Failure is { } failure)
            {
                if (!await DeadLetterAsync(result, failure, generation, ct).ConfigureAwait(false))
                {
                    Abandon(result);
                    return;
                }

                Buffer(result, null, []);
                ExitOutage();
                return;
            }

            Buffer(result, outcome.Envelope, outcome.Rows);
            ExitOutage();
            return;
        }
    }

    /// <summary>
    /// Inserts the buffered rows (retrying a failing insert for as long as it takes, paused), records the
    /// metrics, then commits the offsets of every buffered record. Throws only on cancellation (and, in
    /// rebuild mode, when the commit fails).
    /// </summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        var backoff = _runtime.Options.TransientInitialBackoff;
        while (_buffer.Count > 0)
        {
            // Re-read the buffer on every attempt: a rebalance during the wait may have dropped part of it.
            var rows = _buffer.SelectMany(b => b.Rows).ToList();
            if (rows.Count == 0)
            {
                break;
            }

            try
            {
                await _runtime.Sink.InsertAsync(_projection, rows, ct).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _runtime.Metrics.Error(_projection.Name, ProjectionMetrics.ErrorKindInsert);
                LogInsertFailed(_logger, ex, GroupId, rows.Count, _projection.TableName, backoff);
                await EnterOutageAndWaitAsync(ConsumerLoopState.DependencyUnavailable, ex, backoff, ct).ConfigureAwait(false);
                backoff = ProjectionRuntime.NextBackoff(backoff, _runtime.Options.TransientMaxBackoff);
            }
        }

        if (_buffer.Count == 0)
        {
            ExitOutage();
            return;
        }

        var completedAt = _runtime.TimeProvider.GetUtcNow();
        var inserted = 0;
        foreach (var record in _buffer)
        {
            if (record.Rows.Count == 0)
            {
                continue;
            }

            inserted += record.Rows.Count;
            _rowsPerTopic[record.Topic] = _rowsPerTopic.GetValueOrDefault(record.Topic) + record.Rows.Count;
            var lag = completedAt - record.OccurredAt!.Value;
            for (var i = 0; i < record.Rows.Count; i++)
            {
                _runtime.Metrics.Lag(_projection.Name, lag);
            }
        }

        if (inserted > 0)
        {
            _runtime.Metrics.Rows(_projection.Name, inserted);
            _runtime.Metrics.BatchSize(_projection.Name, inserted);
        }

        var offsets = _buffer
            .GroupBy(b => b.TopicPartition)
            .Select(g => new TopicPartitionOffset(g.Key, new Offset(g.Max(b => b.Offset) + 1)))
            .ToList();
        var records = _buffer.Count;
        _buffer.Clear();
        _bufferedRows = 0;
        Commit(offsets);
        LogFlushed(_logger, GroupId, inserted, records, offsets.Count);
        ExitOutage();
    }

    /// <summary>
    /// Waits <paramref name="delay"/> while polling the PAUSED consumer: the group membership survives
    /// <c>max.poll.interval.ms</c> and rebalances are served (revocations reach <see cref="OnPartitionsRevoked"/>).
    /// A record that still shows up (a partition assigned during the pause) is rewound and its partition
    /// paused, so it is consumed after the resume, never skipped.
    /// </summary>
    public async Task WaitKeepingConsumerAliveAsync(TimeSpan delay, CancellationToken ct)
    {
        var wait = Task.Delay(delay, _runtime.TimeProvider, ct);
        while (!wait.IsCompleted)
        {
            try
            {
                var stray = _consumer.Consume(KeepAlivePollTimeout);
                if (stray is { IsPartitionEOF: false, Message: not null })
                {
                    _consumer.Seek(stray.TopicPartitionOffset);
                    _consumer.Pause([stray.TopicPartition]);
                }
            }
            catch (ConsumeException e) when (!e.Error.IsFatal)
            {
                LogPollError(_logger, GroupId, e.Error.Code, e.Error.Reason);
            }
        }

        await wait.ConfigureAwait(false); // throws if cancelled
    }

    private async Task<MapOutcome> MapWithRetryAsync(ConsumeResult<string?, byte[]?> result, CancellationToken ct)
    {
        EventEnvelope? envelope = null;
        IReadOnlyList<object?[]> rows = [];
        Exception? transient = null;
        Exception? failure = null;
        var attempt = 0;

        await _retry.ExecuteWithRetryAsync(
            async token =>
            {
                attempt++;
                try
                {
                    var headers = result.Message.Headers ?? new Headers();
                    var parsed = EventEnvelope.FromHeaders(headers);

                    GenericRecord? payload = null;
                    if (result.Message.Value is { } raw)
                    {
                        try
                        {
                            payload = await _deserializer.DeserializeAsync(
                                raw,
                                isNull: false,
                                new SerializationContext(MessageComponentType.Value, result.Topic, headers)).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested && FailureClassifier.IsTransientSchemaRegistryFailure(ex))
                        {
                            // Not this record's fault: leave the retry loop without spending attempts or
                            // reaching the DLQ; ProcessAsync pauses and tries again.
                            transient = ex;
                            return;
                        }
                    }

                    var mapped = _projection.Map(new ProjectionRecord(result.Topic, parsed, payload, _batchIngestedAt))
                        ?? throw new InvalidOperationException($"Projection '{_projection.Name}' returned null rows.");
                    foreach (var row in mapped)
                    {
                        if (row is null || row.Length != _projection.ColumnNames.Count)
                        {
                            throw new InvalidOperationException(
                                $"Projection '{_projection.Name}' returned a row with {row?.Length ?? 0} values for {_projection.ColumnNames.Count} columns.");
                        }
                    }

                    envelope = parsed;
                    rows = mapped;
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    _runtime.Metrics.Error(_projection.Name, ProjectionMetrics.ErrorKindRecord);
                    LogAttemptFailed(_logger, ex, GroupId, result.Topic, result.Partition.Value, result.Offset.Value, attempt);
                    throw;
                }
            },
            ex =>
            {
                failure = ex;
                return Task.CompletedTask;
            },
            ct).ConfigureAwait(false);

        return new MapOutcome(envelope, rows, transient, failure);
    }

    /// <summary>
    /// Publishes the record to its DLQ, retrying (paused, Unhealthy, keep-alive) until it succeeds.
    /// Returns false when the record's partition was revoked during that wait: the record is then
    /// abandoned (re-read by the partition's next owner, which may dead-letter it again).
    /// </summary>
    private async Task<bool> DeadLetterAsync(ConsumeResult<string?, byte[]?> result, Exception failure, long generation, CancellationToken ct)
    {
        var backoff = _runtime.RetryOptions.DlqPublishInitialBackoff;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _runtime.DlqPublisher.PublishAsync(
                    result.Topic,
                    result.Message.Key,
                    result.Message.Value,
                    result.Message.Headers,
                    failure,
                    GroupId,
                    ct).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _runtime.Metrics.Error(_projection.Name, ProjectionMetrics.ErrorKindDlq);
                LogDlqPublishFailed(_logger, ex, GroupId, result.Topic, result.Partition.Value, result.Offset.Value, attempt, backoff);
                await EnterOutageAndWaitAsync(ConsumerLoopState.DlqUnavailable, ex, backoff, ct).ConfigureAwait(false);
                backoff = ProjectionRuntime.NextBackoff(backoff, _runtime.RetryOptions.DlqPublishMaxBackoff);
            }
        }

        _runtime.Metrics.DeadLettered(_projection.Name);
        LogSentToDlq(_logger, failure, GroupId, result.Topic, result.Partition.Value, result.Offset.Value);
        return Generation(result.TopicPartition) == generation;
    }

    private async Task EnterOutageAndWaitAsync(ConsumerLoopState state, Exception error, TimeSpan delay, CancellationToken ct)
    {
        _runtime.Health.Report(GroupId, state, error);
        if (!_paused)
        {
            _consumer.Pause(_consumer.Assignment);
            _paused = true;
        }

        await WaitKeepingConsumerAliveAsync(delay, ct).ConfigureAwait(false);
    }

    private void ExitOutage()
    {
        if (!_paused)
        {
            return;
        }

        // The CURRENT assignment: partitions revoked meanwhile are gone, new ones were paused as strays.
        var resumable = _consumer.Assignment.Where(tp => !_held.Contains(tp)).ToList();
        _consumer.Resume(resumable);
        _paused = false;
        _runtime.Health.Report(GroupId, ConsumerLoopState.Running);
        LogResumed(_logger, GroupId);
    }

    private void Abandon(ConsumeResult<string?, byte[]?> result)
    {
        LogAbandoned(_logger, GroupId, result.Topic, result.Partition.Value, result.Offset.Value);
        ExitOutage();
    }

    private void Commit(List<TopicPartitionOffset> offsets)
    {
        try
        {
            _consumer.Commit(offsets);
        }
        catch (KafkaException ex)
        {
            _runtime.Metrics.Error(_projection.Name, ProjectionMetrics.ErrorKindCommit);
            if (_commitFailureIsFatal)
            {
                throw;
            }

            // E.g. a rebalance took the partition. The rows are in ClickHouse; the records are re-read by
            // the next owner and their rows collapse in the ReplacingMergeTree.
            LogCommitFailed(_logger, ex, GroupId, offsets.Count);
        }
    }

    private void StartBatch()
    {
        _batchStartedAt = _runtime.TimeProvider.GetTimestamp();
        var now = _runtime.TimeProvider.GetUtcNow().UtcTicks;
        _batchIngestedAt = new DateTime(now - (now % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }

    private void Buffer(ConsumeResult<string?, byte[]?> result, EventEnvelope? envelope, IReadOnlyList<object?[]> rows)
    {
        _buffer.Add(new BufferedRecord(result.TopicPartition, result.Topic, result.Offset.Value, envelope?.OccurredAt, rows));
        _bufferedRows += rows.Count;
    }

    private long Generation(TopicPartition tp) => _generations.GetValueOrDefault(tp);

    private sealed record BufferedRecord(
        TopicPartition TopicPartition,
        string Topic,
        long Offset,
        DateTimeOffset? OccurredAt,
        IReadOnlyList<object?[]> Rows);

    private sealed record MapOutcome(
        EventEnvelope? Envelope,
        IReadOnlyList<object?[]> Rows,
        Exception? Transient,
        Exception? Failure);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Projection {GroupId}: attempt {Attempt} failed for {Topic}[{Partition}]@{Offset}")]
    private static partial void LogAttemptFailed(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset, int attempt);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Projection {GroupId}: {Topic}[{Partition}]@{Offset} exhausted its retries and was sent to the DLQ")]
    private static partial void LogSentToDlq(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Projection {GroupId}: DLQ publish #{Attempt} of {Topic}[{Partition}]@{Offset} failed; consumption paused, retrying in {Backoff}")]
    private static partial void LogDlqPublishFailed(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset, int attempt, TimeSpan backoff);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Projection {GroupId}: Schema Registry unavailable while decoding {Topic}[{Partition}]@{Offset}; consumption paused, retrying in {Backoff}")]
    private static partial void LogSchemaRegistryUnavailable(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset, TimeSpan backoff);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Projection {GroupId}: insert of {Rows} row(s) into {Table} failed; nothing committed, consumption paused, retrying in {Backoff}")]
    private static partial void LogInsertFailed(ILogger logger, Exception ex, string groupId, int rows, string table, TimeSpan backoff);

    [LoggerMessage(Level = LogLevel.Information, Message = "Projection {GroupId}: dependency reachable again, consumption resumed")]
    private static partial void LogResumed(ILogger logger, string groupId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Projection {GroupId}: partition {Topic}[{Partition}] was revoked while {Offset} waited; the record is left to the partition's next owner")]
    private static partial void LogAbandoned(ILogger logger, string groupId, string topic, int partition, long offset);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Projection {GroupId}: rebalance dropped {Records} buffered record(s) of {Partitions} revoked partition(s) without commit")]
    private static partial void LogDroppedOnRevoke(ILogger logger, string groupId, int records, int partitions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Projection {GroupId}: record of dead-letter topic {Topic} skipped")]
    private static partial void LogDlqTopicSkipped(ILogger logger, string groupId, string topic);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Projection {GroupId}: offset commit of {Partitions} partition(s) failed after the insert; the records will be re-read and their rows collapse")]
    private static partial void LogCommitFailed(ILogger logger, Exception ex, string groupId, int partitions);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Projection {GroupId}: flushed {Rows} row(s) from {Records} record(s), committed {Partitions} partition(s)")]
    private static partial void LogFlushed(ILogger logger, string groupId, int rows, int records, int partitions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Projection {GroupId}: poll failed with {ErrorCode} ({Reason}); retrying")]
    private static partial void LogPollError(ILogger logger, string groupId, ErrorCode errorCode, string reason);
}
