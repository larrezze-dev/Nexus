using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Projections;

/// <summary>
/// <c>nexus_analytics.dim_legal_entity</c>: latest state per legal entity. Every
/// <c>org.legal-entity.*</c> event carries the full state (EV-R4), so each event is one full row; the
/// table is ReplacingMergeTree(_aggregate_version), which makes re-delivery idempotent and keeps the
/// highest version when events arrive out of order. Deactivation is <c>is_active = false</c>, never a delete.
/// </summary>
public sealed class DimLegalEntityProjection : IProjection
{
    private static readonly string[] Columns =
    [
        "legal_entity_id", "party_id", "group_id", "code", "currency_code", "country_code",
        "is_active", "_event_id", "_aggregate_version", "_ingested_at",
    ];

    public string Name => "dim_legal_entity";
    public string TableName => "nexus_analytics.dim_legal_entity";
    public IReadOnlyList<string> ColumnNames => Columns;

    public ProjectionSubscription Subscription { get; } = ProjectionSubscription.ForTopics(
        "nexus.org.legal-entity.created", "nexus.org.legal-entity.updated", "nexus.org.legal-entity.deactivated");

    public IReadOnlyList<object?[]> Map(ProjectionRecord record)
    {
        var p = record.Payload ?? throw new InvalidOperationException("Legal entity event without payload.");
        return
        [
            [
                PayloadReader.Guid(p, "id"),
                PayloadReader.Guid(p, "partyId"),
                PayloadReader.Guid(p, "groupId"),
                PayloadReader.String(p, "code"),
                PayloadReader.String(p, "currencyCode"),
                PayloadReader.String(p, "countryCode"),
                PayloadReader.Bool(p, "isActive"),
                record.Envelope.EventId,
                PayloadReader.Version(record.Envelope.AggregateVersion),
                record.IngestedAt,
            ],
        ];
    }
}
