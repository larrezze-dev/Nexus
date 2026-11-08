using System.Data;
using System.Text.Json;
using Nexus.BuildingBlocks.Analytics.Querying;
using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.BuildingBlocks.Analytics.Audit;

/// <summary>
/// The audit page of one aggregate over <c>nexus_analytics.event_log FINAL</c> (two scoped
/// statements: the page and the visible total).
/// <list type="bullet">
///   <item><c>FINAL</c>: re-deliveries of the same event are separate rows until a background
///   merge collapses them (<c>ReplacingMergeTree(_ingested_at)</c> on
///   <c>(aggregate_type, aggregate_id, aggregate_version, event_id)</c>); <c>FINAL</c> collapses
///   them at read time, for the page and for the count alike.</item>
///   <item>Order <c>aggregate_version DESC, event_id DESC</c> (the second key only makes the
///   order total).</item>
///   <item><c>previousPayload</c>: the page fetches <c>pageSize + 1</c> rows; in descending order,
///   the row after an item is the immediately previous <i>visible</i> version of the aggregate,
///   which also covers the last item of the page. Computed after the scope filter on purpose: a
///   version the caller cannot see never leaks through its successor's
///   <c>previousPayload</c>.</item>
///   <item>PII (EV-R5): <c>payload_json = ''</c> means a <c>pii: true</c> topic. Such an item gets
///   <c>payload</c> and <c>previousPayload</c> null, and the item after it (newer) gets
///   <c>previousPayload</c> null as well, because what it would point to is a PII payload.</item>
/// </list>
/// The page and the count are two reads, so an event ingested between them can make
/// <c>total</c> differ by one from the rows seen; acceptable for an audit listing.
/// </summary>
internal sealed class AuditEventQuery : IAnalyticsQuery<PagedResult<AuditEventRecord>>
{
    private const string Where =
        """
        WHERE aggregate_type = {aggregate_type:String}
          AND aggregate_id = {aggregate_id:UUID}
          AND {__scope_predicate}
        """;

    private const string PageSql =
        """
        SELECT event_id, occurred_at, event_type, aggregate_version, actor_user_id, correlation_id, payload_json
        FROM nexus_analytics.event_log FINAL
        """ + "\n" + Where + "\n" +
        """
        ORDER BY aggregate_version DESC, event_id DESC
        LIMIT {limit:UInt64} OFFSET {offset:UInt64}
        """;

    private const string CountSql =
        "SELECT count() FROM nexus_analytics.event_log FINAL\n" + Where;

    private readonly int _page;
    private readonly int _pageSize;
    private readonly AnalyticsStatement _pageStatement;
    private readonly AnalyticsStatement _countStatement;

    public AuditEventQuery(string aggregateType, Guid aggregateId, int page, int pageSize)
    {
        _page = page;
        _pageSize = pageSize;

        var filter = new Dictionary<string, object>
        {
            ["aggregate_type"] = aggregateType,
            ["aggregate_id"] = aggregateId,
        };

        _pageStatement = new AnalyticsStatement(
            PageSql,
            new Dictionary<string, object>(filter)
            {
                ["limit"] = (ulong)pageSize + 1,
                ["offset"] = (ulong)(page - 1) * (ulong)pageSize,
            });
        _countStatement = new AnalyticsStatement(CountSql, filter);
        EmptyResult = new PagedResult<AuditEventRecord>([], page, pageSize, 0);
    }

    public PagedResult<AuditEventRecord> EmptyResult { get; }

    public async Task<PagedResult<AuditEventRecord>> ExecuteAsync(IAnalyticsSession session, CancellationToken cancellationToken)
    {
        var rows = await session.QueryAsync(_pageStatement, ReadRow, cancellationToken).ConfigureAwait(false);
        var total = (await session.QueryAsync(_countStatement, r => checked((long)(ulong)r.GetValue(0)), cancellationToken).ConfigureAwait(false))
            .Single();

        var count = Math.Min(rows.Count, _pageSize);
        var items = new List<AuditEventRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var row = rows[i];
            var payload = row.Payload;
            var previous = payload is not null && i + 1 < rows.Count ? rows[i + 1].Payload : null;
            items.Add(new AuditEventRecord(
                row.EventId,
                row.OccurredAt,
                row.EventType,
                row.AggregateVersion,
                row.ActorUserId,
                row.CorrelationId,
                payload,
                previous));
        }

        return new PagedResult<AuditEventRecord>(items, _page, _pageSize, total);
    }

    private static Row ReadRow(IDataRecord record) => new(
        (Guid)record.GetValue(0),
        ToUtc(record.GetValue(1)),
        (string)record.GetValue(2),
        checked((long)(ulong)record.GetValue(3)),
        (Guid)record.GetValue(4),
        (Guid)record.GetValue(5),
        ParsePayload((string)record.GetValue(6)));

    private static DateTimeOffset ToUtc(object value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),

        // The column is DateTime64(6, 'UTC'): the value is a UTC instant whatever Kind the
        // client tags it with (it may come back Unspecified).
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => throw new InvalidCastException($"Unexpected occurred_at value of type {value.GetType()}."),
    };

    private static JsonElement? ParsePayload(string payloadJson)
    {
        if (payloadJson.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.Clone();
    }

    private sealed record Row(
        Guid EventId,
        DateTimeOffset OccurredAt,
        string EventType,
        long AggregateVersion,
        Guid ActorUserId,
        Guid CorrelationId,
        JsonElement? Payload);
}
