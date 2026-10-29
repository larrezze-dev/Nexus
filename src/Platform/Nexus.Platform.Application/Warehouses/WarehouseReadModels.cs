using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Warehouses;

namespace Nexus.Platform.Application.Warehouses;

/// <summary>Detail read model of a warehouse (<c>Warehouse</c> schema) plus its <see cref="Version"/> for the <c>ETag</c>.</summary>
public sealed record WarehouseDetails(
    Guid Id,
    Guid LocationId,
    Guid LegalEntityId,
    Guid? BusinessUnitId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static WarehouseDetails From(Warehouse warehouse, RowMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(warehouse);

        return new WarehouseDetails(
            warehouse.Id, warehouse.LocationId, warehouse.LegalEntityId, warehouse.BusinessUnitId,
            warehouse.Code, warehouse.CaptionI18n, warehouse.IsActive,
            metadata.CreatedAt.UtcDateTime, metadata.UpdatedAt.UtcDateTime, metadata.Version);
    }
}

/// <summary>List read model of a warehouse (<c>WarehouseListItem</c> schema).</summary>
public sealed record WarehouseListItem(
    Guid Id,
    Guid LegalEntityId,
    Guid? BusinessUnitId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    bool IsActive);
