using System.Diagnostics.Metrics;

namespace Nexus.OutboxRelay;

/// <summary>
/// Metrics of the relay (meter <see cref="MeterName"/>). Step 0.7 B.5 adds the pending gauge,
/// the <c>created_at → published_at</c> latency histogram, batch size and errors here.
/// </summary>
public sealed class RelayMetrics
{
    public const string MeterName = "Nexus.OutboxRelay";

    public const string FailedTotalName = "nexus_outbox_failed_total";

    public const string PendingName = "nexus_outbox_pending";
    public const string PublishLatencyName = "nexus_outbox_publish_latency_seconds";
    public const string BatchSizeName = "nexus_outbox_batch_size";
    public const string ErrorsTotalName = "nexus_outbox_errors_total";
    public const string PurgedTotalName = "nexus_outbox_purged_total";

    /// <summary>Value of the <c>kind</c> tag of <c>nexus_outbox_errors_total</c>: Kafka / Schema Registry unavailable.</summary>
    public const string ErrorKindTemporary = "temporary";

    /// <summary>Value of the <c>kind</c> tag of <c>nexus_outbox_errors_total</c>: the row itself could not be published.</summary>
    public const string ErrorKindRow = "row";

    private readonly Counter<long> _failed;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _purged;
    private readonly Histogram<double> _latency;
    private readonly Histogram<int> _batchSize;
    private long _pending;

    public RelayMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _failed = meter.CreateCounter<long>(
            FailedTotalName,
            unit: "{message}",
            description: "Outbox messages marked FAILED after exhausting their publish attempts (alert: the aggregate is blocked).");
        _errors = meter.CreateCounter<long>(
            ErrorsTotalName,
            unit: "{attempt}",
            description: "Failed publish attempts, by kind: temporary (Kafka or Schema Registry unavailable) or row (this row cannot be published).");
        _purged = meter.CreateCounter<long>(
            PurgedTotalName,
            unit: "{message}",
            description: "PUBLISHED outbox rows deleted by the daily purge (conventions-events.md §4.6).");
        _latency = meter.CreateHistogram<double>(
            PublishLatencyName,
            unit: "s",
            description: "Seconds between outbox.message.created_at and published_at, recorded on every successful publish.");
        _batchSize = meter.CreateHistogram<int>(
            BatchSizeName,
            unit: "{message}",
            description: "Rows locked by each non-empty batch.");
        meter.CreateObservableGauge(
            PendingName,
            () => Volatile.Read(ref _pending),
            unit: "{message}",
            description: "Outbox rows in PENDING, refreshed every few seconds by the leader.");
    }

    /// <summary>Latest count of PENDING rows (written by the leader, read by the gauge).</summary>
    public void SetPending(long count) => Volatile.Write(ref _pending, count);

    public void PublishLatency(TimeSpan latency) => _latency.Record(Math.Max(0, latency.TotalSeconds));

    public void BatchSize(int rows) => _batchSize.Record(rows);

    /// <summary>One failed publish attempt; <paramref name="kind"/> is <see cref="ErrorKindTemporary"/> or <see cref="ErrorKindRow"/>.</summary>
    public void Error(string kind) => _errors.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public void Purged(long rows) => _purged.Add(rows);

    /// <summary>A message reached <c>FAILED</c> (conventions-events.md §4.5).</summary>
    public void MessageFailed(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        // Low-cardinality tags only (never ids).
        _failed.Add(1, new KeyValuePair<string, object?>("topic", message.Topic), new KeyValuePair<string, object?>("event_type", message.EventType));
    }
}
