using System.Diagnostics.Metrics;

namespace Nexus.Projections.Runtime;

/// <summary>
/// Metrics of <c>nexus-projections</c> (meter <see cref="MeterName"/>, LOG-R45): per-row lag between the
/// event's <c>occurred_at</c> and the moment its batch insert completed, rows written, records sent to
/// the DLQ, errors by kind and batch size. Every instrument is tagged <c>projection</c> (a handful of
/// fixed names: low cardinality).
/// </summary>
public sealed class ProjectionMetrics
{
    public const string MeterName = "Nexus.Projections";

    public const string LagName = "nexus_projection_lag_seconds";
    public const string RowsTotalName = "nexus_projection_rows_total";
    public const string DlqTotalName = "nexus_projection_dlq_total";
    public const string ErrorsTotalName = "nexus_projection_errors_total";
    public const string BatchSizeName = "nexus_projection_batch_size";

    /// <summary><c>kind</c> of <see cref="ErrorsTotalName"/>: one failed attempt to map a record (envelope, Avro bytes, <c>Map</c>).</summary>
    public const string ErrorKindRecord = "record";

    /// <summary><c>kind</c> of <see cref="ErrorsTotalName"/>: a ClickHouse batch insert failed because ClickHouse is unreachable, timing out, 5xx or the table is missing (retried, never dead-lettered).</summary>
    public const string ErrorKindInsertUnavailable = "insert_unavailable";

    /// <summary>
    /// <c>kind</c> of <see cref="ErrorsTotalName"/>: ClickHouse (or the client's RowBinary serialization) REJECTED
    /// the batch — type, parse or value errors: a projection bug (decision D1). The projection stays blocked
    /// (never committed, never dead-lettered) until it is fixed; alert on any increase.
    /// </summary>
    public const string ErrorKindInsertRejected = "insert_rejected";

    /// <summary><c>kind</c> of <see cref="ErrorsTotalName"/>: the Schema Registry could not be reached (transient).</summary>
    public const string ErrorKindSchemaRegistry = "schema_registry";

    /// <summary><c>kind</c> of <see cref="ErrorsTotalName"/>: a DLQ publish failed (the record is retried, never skipped).</summary>
    public const string ErrorKindDlq = "dlq";

    /// <summary><c>kind</c> of <see cref="ErrorsTotalName"/>: a non-fatal Kafka poll error.</summary>
    public const string ErrorKindPoll = "poll";

    /// <summary><c>kind</c> of <see cref="ErrorsTotalName"/>: an offset commit failed after a successful insert (the rows are re-inserted on redelivery and collapse in ClickHouse).</summary>
    public const string ErrorKindCommit = "commit";

    /// <summary>
    /// Bucket boundaries (seconds) of <see cref="LagName"/>: around the LOG-R45 target (p95 &lt; 5 s), from
    /// 50 ms to 5 min (a back-log being drained, or a rebuild).
    /// </summary>
    public static readonly IReadOnlyList<double> LagBuckets =
        [0.05, 0.1, 0.25, 0.5, 1, 1.5, 2, 3, 5, 10, 30, 60, 300];

    /// <summary>Bucket boundaries (rows) of <see cref="BatchSizeName"/>, up to the default batch size.</summary>
    public static readonly IReadOnlyList<int> BatchSizeBuckets = [1, 5, 10, 25, 50, 100, 250, 500, 1000];

    private readonly Histogram<double> _lag;
    private readonly Counter<long> _rows;
    private readonly Counter<long> _dlq;
    private readonly Counter<long> _errors;
    private readonly Histogram<int> _batchSize;

    public ProjectionMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _lag = meter.CreateHistogram(
            LagName,
            unit: "s",
            description: "Seconds between an event's occurred_at and the completion of the ClickHouse insert of its row, one measurement per row (LOG-R45: p95 < 5 s).",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = LagBuckets });
        _rows = meter.CreateCounter<long>(
            RowsTotalName,
            unit: "{row}",
            description: "Rows inserted into ClickHouse by the projection.");
        _dlq = meter.CreateCounter<long>(
            DlqTotalName,
            unit: "{message}",
            description: "Records the projection could not map after every retry, forwarded to {topic}.DLQ.");
        _errors = meter.CreateCounter<long>(
            ErrorsTotalName,
            unit: "{error}",
            description: "Failures by kind: record (one mapping attempt), insert_unavailable (ClickHouse unreachable), insert_rejected (ClickHouse refused the batch: projection bug, blocked), schema_registry, dlq (DLQ publish), poll, commit.");
        _batchSize = meter.CreateHistogram(
            BatchSizeName,
            unit: "{row}",
            description: "Rows written by each non-empty ClickHouse insert.",
            tags: null,
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = BatchSizeBuckets });
    }

    /// <summary>One inserted row whose event occurred <paramref name="lag"/> before the insert completed.</summary>
    public void Lag(string projection, TimeSpan lag) =>
        _lag.Record(Math.Max(0, lag.TotalSeconds), Tag(projection));

    public void Rows(string projection, long rows) => _rows.Add(rows, Tag(projection));

    public void BatchSize(string projection, int rows) => _batchSize.Record(rows, Tag(projection));

    public void DeadLettered(string projection) => _dlq.Add(1, Tag(projection));

    public void Error(string projection, string kind) =>
        _errors.Add(1, Tag(projection), new KeyValuePair<string, object?>("kind", kind));

    private static KeyValuePair<string, object?> Tag(string projection) => new("projection", projection);
}
