using Nexus.Platform.Application.Parties;

namespace Nexus.Platform.Api.Parties;

// HTTP shapes of contracts/openapi/platform.yaml (Parties). PascalCase here, camelCase on the
// wire (JsonSerializerDefaults.Web). `required` marks the contract's required request
// properties: the JSON binder rejects a body without them (400) and the generated OpenAPI
// document lists them as required, like the contract.

/// <summary><c>CreatePartyRequest</c>.</summary>
public sealed record CreatePartyRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required string PartyType { get; init; }

    public required string LegalName { get; init; }

    public string? DisplayName { get; init; }

    public string? TaxId { get; init; }

    public string? ErpCompanyCode { get; init; }

    public bool IsInternal { get; init; }
}

/// <summary><c>UpdatePartyRequest</c> (full replacement of the editable fields; <c>partyType</c>
/// and <c>isInternal</c> are immutable, ORG-R2, and are not here).</summary>
public sealed record UpdatePartyRequest
{
    public required string LegalName { get; init; }

    public string? DisplayName { get; init; }

    public string? TaxId { get; init; }

    public string? ErpCompanyCode { get; init; }
}

/// <summary><c>Party</c>.</summary>
public sealed record PartyResponse(
    Guid Id,
    string PartyType,
    string LegalName,
    string? DisplayName,
    string? TaxId,
    string? ErpCompanyCode,
    bool IsInternal,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static PartyResponse From(PartyDetails party)
    {
        ArgumentNullException.ThrowIfNull(party);
        return new PartyResponse(
            party.Id, party.PartyType, party.LegalName, party.DisplayName, party.TaxId, party.ErpCompanyCode,
            party.IsInternal, party.IsActive, party.CreatedAt, party.UpdatedAt);
    }
}

/// <summary><c>PartyListItem</c>.</summary>
public sealed record PartyListItemResponse(
    Guid Id,
    string PartyType,
    string LegalName,
    string? DisplayName,
    bool IsInternal,
    bool IsActive);
