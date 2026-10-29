using Nexus.Platform.Application.BusinessUnits;

namespace Nexus.Platform.Api.BusinessUnits;

// HTTP shapes of contracts/openapi/platform.yaml (BusinessUnits). PascalCase here, camelCase on
// the wire (JsonSerializerDefaults.Web).

/// <summary><c>CreateBusinessUnitRequest</c>.</summary>
public sealed record CreateBusinessUnitRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required string LegalName { get; init; }

    public string? DisplayName { get; init; }

    public required Guid LegalEntityId { get; init; }

    public required string Code { get; init; }

    public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }

    public string? CostCenterCode { get; init; }
}

/// <summary><c>UpdateBusinessUnitRequest</c>.</summary>
public sealed record UpdateBusinessUnitRequest
{
    public required string Code { get; init; }

    public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }

    public string? CostCenterCode { get; init; }
}

/// <summary><c>BusinessUnit</c>.</summary>
public sealed record BusinessUnitResponse(
    Guid Id,
    Guid PartyId,
    Guid LegalEntityId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? CostCenterCode,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static BusinessUnitResponse From(BusinessUnitDetails businessUnit)
    {
        ArgumentNullException.ThrowIfNull(businessUnit);
        return new BusinessUnitResponse(
            businessUnit.Id, businessUnit.PartyId, businessUnit.LegalEntityId, businessUnit.Code,
            businessUnit.CaptionI18n, businessUnit.CostCenterCode, businessUnit.IsActive,
            businessUnit.CreatedAt, businessUnit.UpdatedAt);
    }
}

/// <summary><c>BusinessUnitListItem</c>.</summary>
public sealed record BusinessUnitListItemResponse(
    Guid Id,
    Guid LegalEntityId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    bool IsActive);
