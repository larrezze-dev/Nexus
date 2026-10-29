using Nexus.Platform.Application.Warehouses;

namespace Nexus.Platform.Api.Warehouses;

// HTTP shapes of contracts/openapi/platform.yaml (Warehouses). PascalCase here, camelCase on
// the wire (JsonSerializerDefaults.Web).

/// <summary><c>CreateWarehouseRequest</c>.</summary>
public sealed record CreateWarehouseRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required Guid LocationId { get; init; }

    public required Guid LegalEntityId { get; init; }

    public Guid? BusinessUnitId { get; init; }

    public required string Code { get; init; }

    public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }
}

/// <summary><c>UpdateWarehouseRequest</c>.</summary>
public sealed record UpdateWarehouseRequest
{
    public required Guid LocationId { get; init; }

    public Guid? BusinessUnitId { get; init; }

    public required string Code { get; init; }

    public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }
}

/// <summary><c>Warehouse</c>.</summary>
public sealed record WarehouseResponse(
    Guid Id,
    Guid LocationId,
    Guid LegalEntityId,
    Guid? BusinessUnitId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static WarehouseResponse From(WarehouseDetails warehouse)
    {
        ArgumentNullException.ThrowIfNull(warehouse);
        return new WarehouseResponse(
            warehouse.Id, warehouse.LocationId, warehouse.LegalEntityId, warehouse.BusinessUnitId,
            warehouse.Code, warehouse.CaptionI18n, warehouse.IsActive, warehouse.CreatedAt, warehouse.UpdatedAt);
    }
}

/// <summary><c>WarehouseListItem</c>.</summary>
public sealed record WarehouseListItemResponse(
    Guid Id,
    Guid LegalEntityId,
    Guid? BusinessUnitId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    bool IsActive);
