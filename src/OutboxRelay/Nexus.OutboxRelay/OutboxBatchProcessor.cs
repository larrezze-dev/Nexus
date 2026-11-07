using Microsoft.Extensions.Logging;

namespace Nexus.OutboxRelay;

/// <summary>Outcome of one batch.</summary>
/// <param name="Selected">Rows locked by the batch query.</param>
/// <param name="Published">Rows marked PUBLISHED.</param>
/// <param name="Failed">Rows whose attempt failed (still PENDING or now FAILED).</param>
/// <param name="StoppedOnTransientFailure">The batch stopped early on a <see cref="PublishFailureKind.InfraGlobal"/> failure (Kafka or the Schema Registry unavailable).</param>
/// <param name="StopCause">When stopped: the failure description (category and cause; never payload values).</param>
/// <param name="Deferred">Rows skipped untouched because their topic is in the preflight's negative cache.</param>
public readonly record struct BatchOutcome(int Selected, int Published, int Failed, bool StoppedOnTransientFailure, string? StopCause = null, int Deferred = 0);

/// <summary>
/// Publishes one locked batch and records the outcome of every attempted row in the same
/// transaction (conventions-events.md §4.4–§4.5).
///
/// <para>Per row, in <c>created_at, id</c> order:</para>
/// <list type="bullet">
///   <item>Success: <c>PUBLISHED</c> + <c>published_at</c> (from <see cref="TimeProvider"/>).</item>
///   <item>Any failure: <c>attempts + 1</c> and <c>last_error</c> (truncated), then by
///   <see cref="PublishFailureKind"/> (decision of Luciano, conventions-events.md §4 step 5):
///     <list type="bullet">
///       <item><see cref="PublishFailureKind.InfraGlobal"/> (broker / registry unavailable): the
///       batch stops, the remaining rows are untouched and the caller backs off. Never
///       <c>FAILED</c>, however long the outage.</item>
///       <item><see cref="PublishFailureKind.TopicUnreachable"/> (topic missing or not
///       authorised): only that aggregate is skipped; other aggregates continue. Never
///       <c>FAILED</c>; <c>nexus_outbox_errors_total{kind="topic"}</c> and a warning naming the
///       topic. While the topic is in the preflight's negative cache the row comes back
///       <see cref="PublishResult.Deferred"/>: the aggregate is skipped but the row is not touched
///       (no attempt, no <c>last_error</c>, no log, no metric), so a missing topic costs one
///       attempt per topic per negative TTL (5 s), not one per loop pass.</item>
///       <item><see cref="PublishFailureKind.Row"/>: the aggregate is skipped; at
///       <c>attempts &gt;= MaxAttempts</c> the row becomes <c>FAILED</c> and
///       <c>nexus_outbox_failed_total</c> is incremented.</item>
///     </list>
///   </item>
///   <item><b>EV-R3 guard:</b> after a failure, the rest of that aggregate's rows in this batch are
///   skipped and left untouched (still <c>PENDING</c>, attempts unchanged), so a later version is
///   never published before an earlier one. Across batches, the batch query never selects a row
///   that has an earlier <c>FAILED</c> row of the same aggregate.</item>
/// </list>
///
/// <para>
/// Cancellation (shutdown) is only checked between rows: a row handed to Kafka is always awaited
/// and recorded, and the batch is committed with what was done, so published rows are not
/// re-published by the next leader.
/// </para>
/// </summary>
public sealed partial class OutboxBatchProcessor
{
    /// <summary>Maximum length of <c>last_error</c>.</summary>
    public const int LastErrorMaxLength = 2000;

    private readonly IEventPublisher _publisher;
    private readonly RelayMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxAttempts;
    private readonly ILogger _logger;

    public OutboxBatchProcessor(IEventPublisher publisher, RelayMetrics metrics, TimeProvider timeProvider, int maxAttempts, ILogger logger)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        _publisher = publisher;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _maxAttempts = maxAttempts;
        _logger = logger;
    }

    public async Task<BatchOutcome> ProcessAsync(IOutboxBatch batch, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var blockedAggregates = new HashSet<Guid>();
        int published = 0, failed = 0, deferred = 0;
        string? stopCause = null;

        foreach (var message in batch.Messages)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (blockedAggregates.Contains(message.AggregateId))
            {
                // EV-R3: an earlier row of this aggregate failed in this batch.
                continue;
            }

            // Never publish without a confirmed lock (throws RelayLeadershipLostException: nothing
            // is committed, every row stays as it was and the next leader publishes it).
            await batch.ConfirmLeadershipAsync(CancellationToken.None).ConfigureAwait(false);

            var result = await _publisher.PublishAsync(message, stoppingToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                var publishedAt = _timeProvider.GetUtcNow();
                batch.MarkPublished(message.Id, publishedAt);
                _metrics.PublishLatency(publishedAt - message.CreatedAt);
                published++;
                continue;
            }

            blockedAggregates.Add(message.AggregateId);
            if (!result.CountsAsAttempt)
            {
                // Topic still in the preflight's negative cache: no attempt, no UPDATE, no log, no
                // metric; the aggregate is only held back (EV-R3).
                deferred++;
                continue;
            }

            failed++;
            _metrics.Error(result.Kind switch
            {
                PublishFailureKind.InfraGlobal => RelayMetrics.ErrorKindTemporary,
                PublishFailureKind.TopicUnreachable => RelayMetrics.ErrorKindTopic,
                _ => RelayMetrics.ErrorKindRow,
            });
            var attempts = message.Attempts + 1;
            // Only a failure of this row can make it FAILED (and block its aggregate for good):
            // never an outage, never a missing topic.
            var terminal = result.Kind == PublishFailureKind.Row && attempts >= _maxAttempts;
            var error = Truncate(result.Error);
            batch.MarkFailed(message.Id, attempts, error, terminal);

            if (terminal)
            {
                _metrics.MessageFailed(message);
                LogMessageFailed(_logger, message.Id, message.EventType, message.Topic, attempts);
            }
            else if (result.Kind == PublishFailureKind.TopicUnreachable)
            {
                LogTopicUnreachable(_logger, message.Topic, message.Id, attempts);
            }
            else
            {
                LogAttemptFailed(_logger, message.Id, message.EventType, attempts, result.Kind);
            }

            if (result.Kind == PublishFailureKind.InfraGlobal)
            {
                stopCause = error;
                break;
            }
        }

        // Commit even when stopping: the marks must match what was actually sent.
        await batch.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        if (batch.Messages.Count > 0)
        {
            _metrics.BatchSize(batch.Messages.Count);
        }

        return new BatchOutcome(batch.Messages.Count, published, failed, stopCause is not null, stopCause, deferred);
    }

    /// <summary>Truncates to <see cref="LastErrorMaxLength"/> and strips NUL (not storable in <c>text</c>).</summary>
    public static string Truncate(string? error)
    {
        var text = string.IsNullOrEmpty(error) ? "Unknown publish error." : error.Replace('\0', ' ');
        return text.Length <= LastErrorMaxLength ? text : text[..(LastErrorMaxLength - 1)] + "…";
    }

    // Only ids, types and counters are logged; last_error (which may quote payload values) stays in the database.
    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({EventType} -> {Topic}) marked FAILED after {Attempts} attempts; its aggregate is blocked until the row is reset.")]
    private static partial void LogMessageFailed(ILogger logger, Guid messageId, string eventType, string topic, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing outbox message {MessageId} ({EventType}) failed, attempt {Attempts} ({FailureKind}).")]
    private static partial void LogAttemptFailed(ILogger logger, Guid messageId, string eventType, int attempts, PublishFailureKind failureKind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka topic '{Topic}' does not exist or is not authorised; outbox message {MessageId} held back (attempt {Attempts}, never FAILED). Create the topic or grant the ACL.")]
    private static partial void LogTopicUnreachable(ILogger logger, string topic, Guid messageId, int attempts);
}
