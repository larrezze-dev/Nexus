using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Parties;

namespace Nexus.Platform.Application.Users.DeactivateUser;

/// <summary>
/// <c>POST /v1/users/{id}/deactivate</c> (<c>deactivateUser</c>), guarded by <c>If-Match</c>.
/// Never for SYSTEM (ORG-R6). Idempotent: an already inactive user is returned unchanged,
/// without a new event. The person party stays active (it remains in the directory).
/// <para>
/// Scope: the contract declares <c>x-scope: none</c>. Deactivating a user with no role
/// assignment stays unscoped. For a user who holds assignments, the caller's
/// <c>USER:UPDATE</c> must cover every scope of them (<see cref="UserScopeGuard"/>), 403
/// otherwise: deactivation is effectively irreversible (no reactivate endpoint, and the
/// <c>oid</c> stays taken), so an admin of one legal entity must not be able to lock out the
/// admin of another, or a global administrator.
/// </para>
/// </summary>
public sealed record DeactivateUserCommand(Guid Id, UserVersion ExpectedVersion) : ICommand<UserDetails>;

internal sealed class DeactivateUserHandler : ICommandHandler<DeactivateUserCommand, UserDetails>
{
    private const string UpdatePermission = "USER:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IAppUserRepository _users;
    private readonly IPartyRepository _parties;
    private readonly IUserReadDb _readDb;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public DeactivateUserHandler(
        IRequestTransaction transaction,
        IAppUserRepository users,
        IPartyRepository parties,
        IUserReadDb readDb,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _users = users;
        _parties = parties;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<UserDetails>> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await UserLoader.LoadAsync(_users, _parties, command.Id, command.ExpectedVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<UserDetails>(loaded.Error!);
        }

        var (user, party) = loaded.Value;

        // Before any change (and before ORG-R6 and the idempotent no-op).
        if (!await UserScopeGuard.CoversEveryAssignmentScopeAsync(_readDb, _scopeChecker, UpdatePermission, user.Id, cancellationToken))
        {
            return Result.Failure<UserDetails>(UserErrors.ScopeNotCovered);
        }

        var deactivated = user.Deactivate();
        if (deactivated.IsFailure)
        {
            return Result.Failure<UserDetails>(deactivated.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(UserDetails.From(user, _users.GetMetadata(user), party, _parties.GetMetadata(party)));
    }
}
