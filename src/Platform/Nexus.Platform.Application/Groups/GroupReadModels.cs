using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Groups;

namespace Nexus.Platform.Application.Groups;

/// <summary>
/// Detail read model of a group (<c>Group</c> schema of <c>contracts/openapi/platform.yaml</c>)
/// plus its <see cref="Version"/> for the <c>ETag</c>. Unlike <c>UserDetails</c>, a group's own
/// row carries everything the contract needs (no join to <c>party</c>): the <c>Party</c>
/// created inline has no life of its own once created, and <c>UpdateGroupRequest</c> never
/// touches it. Instants are UTC <see cref="DateTime"/>s (serialized with a <c>Z</c> suffix).
/// </summary>
public sealed record GroupDetails(
    Guid Id,
    Guid PartyId,
    string Code,
    string ConsolidationMethod,
    string ReportingCurrencyCode,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static GroupDetails From(Group group, RowMetadata row)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new GroupDetails(
            group.Id,
            group.PartyId,
            group.Code,
            group.ConsolidationMethod,
            group.ReportingCurrencyCode,
            row.CreatedAt.UtcDateTime,
            row.UpdatedAt.UtcDateTime,
            row.Version);
    }
}

/// <summary>List read model of a group (<c>GroupListItem</c> schema).</summary>
public sealed record GroupListItem(Guid Id, Guid PartyId, string Code, string ReportingCurrencyCode);
