using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Groups;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.Groups;

/// <summary>
/// Detail read model of a group (<c>Group</c> schema of <c>contracts/openapi/platform.yaml</c>)
/// plus its <see cref="Version"/> for the <c>ETag</c>. <see cref="LegalName"/>/<see cref="DisplayName"/>
/// are the group's <c>org.party</c> names (same service, ARC-R1 OK); <c>UpdateGroupRequest</c>
/// never touches them. Instants are UTC <see cref="DateTime"/>s (serialized with a <c>Z</c> suffix).
/// </summary>
public sealed record GroupDetails(
    Guid Id,
    Guid PartyId,
    string LegalName,
    string? DisplayName,
    string Code,
    string ConsolidationMethod,
    string ReportingCurrencyCode,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate, its party and its row metadata.</summary>
    public static GroupDetails From(Group group, Party party, RowMetadata row)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(party);

        return new GroupDetails(
            group.Id,
            group.PartyId,
            party.LegalName,
            party.DisplayName,
            group.Code,
            group.ConsolidationMethod,
            group.ReportingCurrencyCode,
            row.CreatedAt.UtcDateTime,
            row.UpdatedAt.UtcDateTime,
            row.Version);
    }
}

/// <summary>List read model of a group (<c>GroupListItem</c> schema).</summary>
public sealed record GroupListItem(Guid Id, Guid PartyId, string LegalName, string? DisplayName, string Code, string ReportingCurrencyCode);
