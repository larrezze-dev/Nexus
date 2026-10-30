using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.LegalEntities;

namespace Nexus.Platform.Application.LegalEntities;

/// <summary>
/// Detail read model of a legal entity (<c>LegalEntity</c> schema of
/// <c>contracts/openapi/platform.yaml</c>) plus its <see cref="Version"/> for the <c>ETag</c>.
/// Like <c>GroupDetails</c>, the legal entity's own row carries everything the contract needs
/// (no join to <c>party</c>). Instants are UTC <see cref="DateTime"/>s (serialized with a
/// <c>Z</c> suffix).
/// </summary>
public sealed record LegalEntityDetails(
    Guid Id,
    Guid PartyId,
    Guid GroupId,
    string Code,
    string CurrencyCode,
    string CountryCode,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static LegalEntityDetails From(LegalEntity legalEntity, RowMetadata row)
    {
        ArgumentNullException.ThrowIfNull(legalEntity);

        return new LegalEntityDetails(
            legalEntity.Id,
            legalEntity.PartyId,
            legalEntity.GroupId,
            legalEntity.Code,
            legalEntity.CurrencyCode,
            legalEntity.CountryCode,
            legalEntity.IsActive,
            row.CreatedAt.UtcDateTime,
            row.UpdatedAt.UtcDateTime,
            row.Version);
    }
}

/// <summary>List read model of a legal entity (<c>LegalEntityListItem</c> schema).</summary>
public sealed record LegalEntityListItem(Guid Id, Guid GroupId, string Code, string CurrencyCode, string CountryCode, bool IsActive);
