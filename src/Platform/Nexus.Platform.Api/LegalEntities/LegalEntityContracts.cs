using Nexus.Platform.Application.LegalEntities;

namespace Nexus.Platform.Api.LegalEntities;

// HTTP shapes of contracts/openapi/platform.yaml (LegalEntities). PascalCase here, camelCase on
// the wire (JsonSerializerDefaults.Web). `required` marks the contract's required request
// properties: the JSON binder rejects a body without them (400) and the generated OpenAPI
// document lists them as required, like the contract.

/// <summary><c>CreateLegalEntityRequest</c>.</summary>
public sealed record CreateLegalEntityRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required string LegalName { get; init; }

    public string? DisplayName { get; init; }

    public required Guid GroupId { get; init; }

    public required string Code { get; init; }

    public required string CurrencyCode { get; init; }

    public required string CountryCode { get; init; }
}

/// <summary><c>UpdateLegalEntityRequest</c>.</summary>
public sealed record UpdateLegalEntityRequest
{
    public required string Code { get; init; }

    public required string CurrencyCode { get; init; }

    public required string CountryCode { get; init; }
}

/// <summary><c>LegalEntity</c>.</summary>
public sealed record LegalEntityResponse(
    Guid Id,
    Guid PartyId,
    Guid GroupId,
    string Code,
    string CurrencyCode,
    string CountryCode,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static LegalEntityResponse From(LegalEntityDetails legalEntity)
    {
        ArgumentNullException.ThrowIfNull(legalEntity);
        return new LegalEntityResponse(
            legalEntity.Id, legalEntity.PartyId, legalEntity.GroupId, legalEntity.Code, legalEntity.CurrencyCode,
            legalEntity.CountryCode, legalEntity.IsActive, legalEntity.CreatedAt, legalEntity.UpdatedAt);
    }
}

/// <summary><c>LegalEntityListItem</c>.</summary>
public sealed record LegalEntityListItemResponse(Guid Id, Guid GroupId, string Code, string CurrencyCode, string CountryCode, bool IsActive);
