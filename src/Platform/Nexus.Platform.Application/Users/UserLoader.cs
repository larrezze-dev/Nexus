using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Application.Users;

/// <summary>
/// Loads a user and its person party for modification and enforces optimistic concurrency
/// (BE-R3): 404 if the user does not exist, 409 <c>CONCURRENCY</c> if the caller's
/// <c>If-Match</c> version no longer matches either row. A concurrent change committed between
/// this check and the commit is still caught by EF Core's <c>xmin</c> token on the rows being
/// written (also 409).
/// </summary>
internal static class UserLoader
{
    public static async Task<Result<(AppUser User, Party Party)>> LoadAsync(
        IAppUserRepository users,
        IPartyRepository parties,
        Guid id,
        UserVersion expectedVersion,
        CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure<(AppUser, Party)>(UserErrors.NotFound(id));
        }

        var party = await parties.GetByIdAsync(user.PartyId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{user.Id}' references a missing party '{user.PartyId}'.");

        var current = new UserVersion(users.GetMetadata(user).Version, parties.GetMetadata(party).Version);
        if (current != expectedVersion)
        {
            return Result.Failure<(AppUser, Party)>(UserErrors.StaleVersion);
        }

        return Result.Success((user, party));
    }
}
