using Microsoft.Extensions.Logging;

namespace Nexus.OutboxRelay;

/// <summary>Outcome of one batch.</summary>
/// <param name="Selected">Rows locked by the batch query.</param>
/// <param name="Published">Rows marked PUBLISHED.</param>
/// <param name="Failed">Rows whose attempt failed (still PENDING or now FAILED).</param>
/// <param name="StoppedOnTransientFailure">The batch stopped early because Kafka or the Schema Registry is unavailable.</param>
public readonly record struct BatchOutcome(int Selected, int Published, int Failed, bool StoppedOnTransientFailure);

/// <summary>
/// Publishes one locked batch and records the outcome of every attempted row in the same
/// transaction (conventions-events.md §4.4–§4.5).
///
/// <para>Per row, in <c>created_at, id</c> order:</para>
/// <list type="bullet">
///   <item>Success: <c>PUBLISHED</c> + <c>published_at</c> (from <see cref="TimeProvider"/>).</item>
///   <item>Failure: <c>attempts + 1</c> and <c>last_error</c> (truncated). Only a
///   <b>row-specific</b> failure (<see cref="PublishResult.IsTransient"/> false: payload does not
///   decode/encode, local schema missing or not registered, message too large, unknown topic) at
///   <c>attempts &gt;= MaxAttempts</c> makes the row <c>FAILED</c> and increments
///   <c>nexus_outbox_failed_total</c>: <c>terminal = !IsTransient &amp;&amp; attempts &gt;= MaxAttempts</c>.
///   A transient failure (Kafka / Schema Registry unreachable, timeouts) increments
///   <c>attempts</c> and records <c>last_error</c> but never makes the row <c>FAILED</c>, however
///   long the outage (decision D-H1, conventions-events.md §4 step 5).</item>
///   <item><b>EV-R3 guard:</b> after a failure, the rest of that aggregate's rows in this batch are
///   skipped and left untouched (still <c>PENDING</c>, attempts unchanged), so a later version is
///   never published before an earlier one. Other aggregates continue. Across batches, the batch
///   query never selects a row that has an earlier <c>FAILED</c> row of the same aggregate.</item>
///   <item>Transient failure (cluster or registry unavailable): the batch stops after recording the
///   failed row; the remaining rows are untouched and the caller backs off.</item>
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
        int published = 0, failed = 0;
        var stoppedOnTransient = false;

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

            failed++;
            _metrics.Error(result.IsTransient ? RelayMetrics.ErrorKindTemporary : RelayMetrics.ErrorKindRow);
            blockedAggregates.Add(message.AggregateId);
            var attempts = message.Attempts + 1;
            // D-H1: an infrastructure outage must never block an aggregate (FAILED); only a
            // failure of this row does, once it has used up its attempts.
            var terminal = !result.IsTransient && attempts >= _maxAttempts;
            batch.MarkFailed(message.Id, attempts, Truncate(result.Error), terminal);

            if (terminal)
            {
                _metrics.MessageFailed(message);
                LogMessageFailed(_logger, message.Id, message.EventType, message.Topic, attempts);
            }
            else
            {
                LogAttemptFailed(_logger, message.Id, message.EventType, attempts, result.IsTransient);
            }

            if (result.IsTransient)
            {
                stoppedOnTransient = true;
                break;
            }
        }

        // Commit even when stopping: the marks must match what was actually sent.
        await batch.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        if (batch.Messages.Count > 0)
        {
            _metrics.BatchSize(batch.Messages.Count);
        }

        return new BatchOutcome(batch.Messages.Count, published, failed, stoppedOnTransient);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing outbox message {MessageId} ({EventType}) failed, attempt {Attempts} (transient={IsTransient}).")]
    private static partial void LogAttemptFailed(ILogger logger, Guid messageId, string eventType, int attempts, bool isTransient);
}
