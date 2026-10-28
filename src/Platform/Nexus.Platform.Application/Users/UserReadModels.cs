using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Application.Users;

/// <summary>
/// Detail read model of a user (<c>User</c> schema of <c>contracts/openapi/platform.yaml</c>)
/// plus its <see cref="Version"/> for the <c>ETag</c>. <see cref="UpdatedAt"/> is the later of
/// the <c>app_user</c> and <c>party</c> rows, since <see cref="FullName"/> lives in the party.
/// Instants are UTC <see cref="DateTime"/>s (serialized with a <c>Z</c> suffix).
/// </summary>
public sealed record UserDetails(
    Guid Id,
    Guid PartyId,
    string? Oid,
    string FullName,
    string? Email,
    string PreferredLanguage,
    Guid? BusinessUnitId,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    UserVersion Version)
{
    /// <summary>Builds the read model from freshly loaded/saved aggregates and their row metadata.</summary>
    public static UserDetails From(AppUser user, RowMetadata userRow, Party party, RowMetadata partyRow)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(party);

        return new UserDetails(
            user.Id,
            user.PartyId,
            user.ExternalSubject,
            party.LegalName,
            user.Email,
            user.PreferredLanguage,
            user.BusinessUnitId,
            user.IsActive,
            userRow.CreatedAt.UtcDateTime,
            (userRow.UpdatedAt >= partyRow.UpdatedAt ? userRow.UpdatedAt : partyRow.UpdatedAt).UtcDateTime,
            new UserVersion(userRow.Version, partyRow.Version));
    }
}

/// <summary>List read model of a user (<c>UserListItem</c> schema).</summary>
public sealed record UserListItem(
    Guid Id,
    string? Oid,
    string FullName,
    string? Email,
    string PreferredLanguage,
    Guid? BusinessUnitId,
    bool IsActive);
