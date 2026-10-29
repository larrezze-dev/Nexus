using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.Parties;

/// <summary>Detail read model of a party (<c>Party</c> schema of <c>contracts/openapi/platform.yaml</c>)
/// plus its <see cref="Version"/> for the <c>ETag</c>. Instants are UTC <see cref="DateTime"/>s
/// (serialized with a <c>Z</c> suffix).</summary>
public sealed record PartyDetails(
    Guid Id,
    string PartyType,
    string LegalName,
    string? DisplayName,
    string? TaxId,
    string? ErpCompanyCode,
    bool IsInternal,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    PartyVersion Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static PartyDetails From(Party party, RowMetadata row)
    {
        ArgumentNullException.ThrowIfNull(party);

        return new PartyDetails(
            party.Id,
            party.PartyType.ToName(),
            party.LegalName,
            party.DisplayName,
            party.TaxId,
            party.ErpCompanyCode,
            party.IsInternal,
            party.IsActive,
            row.CreatedAt.UtcDateTime,
            row.UpdatedAt.UtcDateTime,
            new PartyVersion(row.Version));
    }
}

/// <summary>List read model of a party (<c>PartyListItem</c> schema).</summary>
public sealed record PartyListItem(
    Guid Id,
    string PartyType,
    string LegalName,
    string? DisplayName,
    bool IsInternal,
    bool IsActive);
