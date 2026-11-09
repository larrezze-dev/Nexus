using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Projections;

/// <summary>
/// <c>nexus_analytics.event_log</c>: one row per domain event of every topic, feeding the audit trail
/// (conventions-events.md §8). Re-delivery collapses through ReplacingMergeTree on
/// (aggregate_type, aggregate_id, aggregate_version, event_id).
/// <list type="bullet">
/// <item><c>legal_entity_id</c>: the <c>nexus-legal-entity-id</c> header if set, else derived from the
/// aggregate (see <see cref="DeriveLegalEntityId"/>). Derived BEFORE the PII drop, from the payload.</item>
/// <item><c>payload_json</c> (EV-R5): empty for PII topics and for topics missing from the registry
/// (fail closed); headers are always stored. A tombstone (null payload) is also empty.</item>
/// <item><c>actor_user_id</c> is NOT nullable in the table: an absent header (system events) is stored as
/// <see cref="Guid.Empty"/> (all zeros).</item>
/// </list>
/// </summary>
public sealed partial class EventLogProjection : IProjection
{
    // Aggregate type literals exactly as the producer emits them (Nexus.Platform.Domain/*/…Events.cs).
    internal const string LegalEntityAggregate = "legal-entity";
    internal const string BusinessUnitAggregate = "business_unit";
    internal const string WarehouseAggregate = "warehouse";

    private static readonly string[] Columns =
    [
        "legal_entity_id", "topic", "event_id", "event_type", "aggregate_type", "aggregate_id",
        "aggregate_version", "occurred_at", "actor_user_id", "correlation_id", "payload_json", "_ingested_at",
    ];

    private readonly TopicRegistry _topics;
    private readonly ILogger<EventLogProjection> _logger;
    private readonly ConcurrentDictionary<string, byte> _warnedUnknown = new(StringComparer.Ordinal);

    public EventLogProjection(TopicRegistry topics, ILogger<EventLogProjection> logger)
    {
        _topics = topics;
        _logger = logger;
    }

    public string Name => "event_log";
    public string TableName => "nexus_analytics.event_log";
    public IReadOnlyList<string> ColumnNames => Columns;

    /// <summary>Exactly three lowercase segments after <c>nexus.</c>: excludes the <c>{topic}.DLQ</c> topics.</summary>
    public ProjectionSubscription Subscription { get; } =
        ProjectionSubscription.ForPattern(@"^nexus\.[a-z0-9-]+\.[a-z0-9-]+\.[a-z0-9-]+$");

    public IReadOnlyList<object?[]> Map(ProjectionRecord record)
    {
        var e = record.Envelope;
        var legalEntityId = DeriveLegalEntityId(record);

        string payloadJson;
        if (record.Payload is null)
        {
            payloadJson = "";
        }
        else if (_topics.IsPii(record.Topic))
        {
            payloadJson = "";
            if (!_topics.IsKnown(record.Topic) && _warnedUnknown.TryAdd(record.Topic, 0))
                LogUnknownTopic(_logger, record.Topic);
        }
        else
        {
            payloadJson = AvroJson.ToJson(record.Payload);
        }

        return
        [
            [
                legalEntityId,
                record.Topic,
                e.EventId,
                e.EventType,
                e.AggregateType,
                e.AggregateId,
                PayloadReader.Version(e.AggregateVersion),
                e.OccurredAt.UtcDateTime,
                e.ActorUserId ?? Guid.Empty,
                e.CorrelationId,
                payloadJson,
                record.IngestedAt,
            ],
        ];
    }

    /// <summary>
    /// Header first; else by aggregate: <c>legal-entity</c> is its own legal entity (the aggregate id),
    /// <c>business_unit</c> and <c>warehouse</c> carry <c>legalEntityId</c> in the payload (missing or
    /// invalid is a contract violation and throws); any other aggregate has none (null, platform-level).
    /// </summary>
    internal static Guid? DeriveLegalEntityId(ProjectionRecord record)
    {
        var e = record.Envelope;
        if (e.LegalEntityId is { } fromHeader) return fromHeader;

        switch (e.AggregateType)
        {
            case LegalEntityAggregate:
                return e.AggregateId;
            case BusinessUnitAggregate:
            case WarehouseAggregate:
                if (record.Payload is null)
                    throw new InvalidOperationException(
                        $"{e.AggregateType} event {e.EventId} has no payload to derive legal_entity_id from.");
                return PayloadReader.Guid(record.Payload, "legalEntityId");
            default:
                return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Topic {Topic} is not in the topic registry; treating it as PII and storing an empty payload (EV-R5).")]
    private static partial void LogUnknownTopic(ILogger logger, string topic);
}
