using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Projections;

/// <summary>
/// <c>nexus_analytics.dim_warehouse</c>: latest state per warehouse. Every <c>org.warehouse.*</c> event
/// carries the full state (EV-R4), including <c>legalEntityId</c> (immutable for a warehouse), so each
/// event is one full row. See <see cref="DimLegalEntityProjection"/> for the versioning semantics.
/// </summary>
public sealed class DimWarehouseProjection : IProjection
{
    private static readonly string[] Columns =
    [
        "legal_entity_id", "warehouse_id", "location_id", "business_unit_id", "code", "caption_i18n",
        "is_active", "_event_id", "_aggregate_version", "_ingested_at",
    ];

    public string Name => "dim_warehouse";
    public string TableName => "nexus_analytics.dim_warehouse";
    public IReadOnlyList<string> ColumnNames => Columns;

    public ProjectionSubscription Subscription { get; } = ProjectionSubscription.ForTopics(
        "nexus.org.warehouse.created", "nexus.org.warehouse.updated", "nexus.org.warehouse.deactivated");

    public IReadOnlyList<object?[]> Map(ProjectionRecord record)
    {
        var p = record.Payload ?? throw new InvalidOperationException("Warehouse event without payload.");
        return
        [
            [
                PayloadReader.Guid(p, "legalEntityId"),
                PayloadReader.Guid(p, "id"),
                PayloadReader.Guid(p, "locationId"),
                PayloadReader.TryGuid(p, "businessUnitId"),
                PayloadReader.String(p, "code"),
                PayloadReader.StringMap(p, "captionI18n"),
                PayloadReader.Bool(p, "isActive"),
                record.Envelope.EventId,
                PayloadReader.Version(record.Envelope.AggregateVersion),
                record.IngestedAt,
            ],
        ];
    }
}
