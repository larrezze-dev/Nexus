using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.BusinessUnits;

namespace Nexus.Platform.Application.BusinessUnits;

/// <summary>Detail read model of a business unit (<c>BusinessUnit</c> schema) plus its <see cref="Version"/> for the <c>ETag</c>.</summary>
public sealed record BusinessUnitDetails(
    Guid Id,
    Guid PartyId,
    Guid LegalEntityId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? CostCenterCode,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static BusinessUnitDetails From(BusinessUnit businessUnit, RowMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(businessUnit);

        return new BusinessUnitDetails(
            businessUnit.Id, businessUnit.PartyId, businessUnit.LegalEntityId, businessUnit.Code,
            businessUnit.CaptionI18n, businessUnit.CostCenterCode, businessUnit.IsActive,
            metadata.CreatedAt.UtcDateTime, metadata.UpdatedAt.UtcDateTime, metadata.Version);
    }
}

/// <summary>List read model of a business unit (<c>BusinessUnitListItem</c> schema).</summary>
public sealed record BusinessUnitListItem(
    Guid Id,
    Guid LegalEntityId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    bool IsActive);
