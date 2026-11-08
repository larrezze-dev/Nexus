using System.Diagnostics.Metrics;

namespace Nexus.OutboxRelay;

/// <summary>
/// Metrics of the relay (meter <see cref="MeterName"/>): FAILED counter, pending gauge,
/// <c>created_at → published_at</c> latency histogram, batch size, errors by kind and purged rows.
/// </summary>
public sealed class RelayMetrics
{
    public const string MeterName = "Nexus.OutboxRelay";

    public const string FailedTotalName = "nexus_outbox_failed_total";

    public const string PendingName = "nexus_outbox_pending";
    public const string OldestPendingAgeName = "nexus_outbox_oldest_pending_age_seconds";
    public const string HeldTopicsName = "nexus_outbox_held_topics";
    public const string PublishLatencyName = "nexus_outbox_publish_latency_seconds";
    public const string BatchSizeName = "nexus_outbox_batch_size";
    public const string ErrorsTotalName = "nexus_outbox_errors_total";
    public const string PurgedTotalName = "nexus_outbox_purged_total";

    /// <summary>Value of the <c>kind</c> tag of <c>nexus_outbox_errors_total</c>: Kafka / Schema Registry unavailable.</summary>
    public const string ErrorKindTemporary = "temporary";

    /// <summary>Value of the <c>kind</c> tag of <c>nexus_outbox_errors_total</c>: the row's topic does not exist or is not authorised.</summary>
    public const string ErrorKindTopic = "topic";

    /// <summary>Value of the <c>kind</c> tag of <c>nexus_outbox_errors_total</c>: the row itself could not be published.</summary>
    public const string ErrorKindRow = "row";

    /// <summary>
    /// Bucket boundaries (seconds) of <see cref="PublishLatencyName"/>: the SDK default
    /// (0 … 10 000, tuned for milliseconds) would put every sub-second latency in one or two
    /// buckets. 5 ms (NOTIFY wake-up) to 60 s (one back-off step at the cap).
    /// </summary>
    public static readonly IReadOnlyList<double> PublishLatencyBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60];

    /// <summary>Bucket boundaries (rows) of <see cref="BatchSizeName"/>, up to twice the default batch size.</summary>
    public static readonly IReadOnlyList<int> BatchSizeBuckets = [1, 5, 10, 25, 50, 100, 250, 500, 1000];

    private readonly Counter<long> _failed;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _purged;
    private readonly Histogram<double> _latency;
    private readonly Histogram<int> _batchSize;
    private long _pending;
    private double _oldestPendingAge;
    private long _held;
    private int _heldKnown;
    private int _pendingKnown; // 1 while the leader publishes a value; 0 = gauge not observed

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
            description: "Failed publish attempts, by kind: temporary (Kafka or Schema Registry unavailable), topic (topic missing or not authorised) or row (this row cannot be published).");
        _purged = meter.CreateCounter<long>(
            PurgedTotalName,
            unit: "{message}",
            description: "PUBLISHED outbox rows deleted by the daily purge (conventions-events.md §4.6).");
        _latency = meter.CreateHistogram(
            PublishLatencyName,
            unit: "s",
            description: "Seconds between outbox.message.created_at and published_at, recorded on every successful publish.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = PublishLatencyBuckets });
        _batchSize = meter.CreateHistogram(
            BatchSizeName,
            unit: "{message}",
            description: "Rows locked by each non-empty batch.",
            tags: null,
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = BatchSizeBuckets });
        meter.CreateObservableGauge(
            PendingName,
            ObservePending,
            unit: "{message}",
            description: "Outbox rows in PENDING, refreshed every few seconds by the leader. Only the leader reports it: a passive instance (or one that lost the lease) reports nothing, so a stale value never lingers.");
        meter.CreateObservableGauge(
            OldestPendingAgeName,
            ObserveOldestPendingAge,
            unit: "s",
            description: "Age of the oldest PENDING outbox row (0 when none), refreshed with nexus_outbox_pending and reported only by the leader. Grows during an outage or while a topic is missing.");
        meter.CreateObservableGauge(
            HeldTopicsName,
            () => Volatile.Read(ref _heldKnown) == 1 ? [new Measurement<long>(Volatile.Read(ref _held))] : Array.Empty<Measurement<long>>(),
            unit: "{topic}",
            description: "Kafka topics held back because they are unreachable (missing or not authorised): their rows, and the later rows of those aggregates, are excluded from batches until the topic answers. Reported only by the leader.");
    }

    /// <summary>
    /// Latest PENDING backlog (written by the leader, read by the gauges): the count and the age in
    /// seconds of the oldest PENDING row (0 when there is none).
    /// </summary>
    public void SetPending(long count, double oldestAgeSeconds = 0)
    {
        Volatile.Write(ref _pending, count);
        Volatile.Write(ref _oldestPendingAge, Math.Max(0, oldestAgeSeconds));
        Volatile.Write(ref _pendingKnown, 1);
    }

    /// <summary>
    /// The instance is no longer (or never was) the leader: both backlog gauges stop reporting until the
    /// next <see cref="SetPending"/>, so dashboards summing the gauge over instances do not count a
    /// former leader's frozen value.
    /// </summary>
    public void ClearPending()
    {
        Volatile.Write(ref _pendingKnown, 0);
        Volatile.Write(ref _heldKnown, 0);
    }

    /// <summary>Current number of held topics (written by the leader after each batch).</summary>
    public void SetHeldTopics(long count)
    {
        Volatile.Write(ref _held, count);
        Volatile.Write(ref _heldKnown, 1);
    }

    public void PublishLatency(TimeSpan latency) => _latency.Record(Math.Max(0, latency.TotalSeconds));

    public void BatchSize(int rows) => _batchSize.Record(rows);

    /// <summary>One failed publish attempt; <paramref name="kind"/> is <see cref="ErrorKindTemporary"/> or <see cref="ErrorKindRow"/>.</summary>
    public void Error(string kind) => _errors.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public void Purged(long rows) => _purged.Add(rows);

    private IEnumerable<Measurement<long>> ObservePending() =>
        Volatile.Read(ref _pendingKnown) == 1 ? [new Measurement<long>(Volatile.Read(ref _pending))] : [];

    private IEnumerable<Measurement<double>> ObserveOldestPendingAge() =>
        Volatile.Read(ref _pendingKnown) == 1 ? [new Measurement<double>(Volatile.Read(ref _oldestPendingAge))] : [];

    /// <summary>A message reached <c>FAILED</c> (conventions-events.md §4.5).</summary>
    public void MessageFailed(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        // Low-cardinality tags only (never ids).
        _failed.Add(1, new KeyValuePair<string, object?>("topic", message.Topic), new KeyValuePair<string, object?>("event_type", message.EventType));
    }
}
