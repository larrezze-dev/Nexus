using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Application.RoleAssignments;

namespace Nexus.Platform.Application.Users.DeactivateUser;

/// <summary>
/// <c>POST /v1/users/{id}/deactivate</c> (<c>deactivateUser</c>), guarded by <c>If-Match</c>.
/// Never for SYSTEM (ORG-R6), never by the user themselves (ACC-R13), never the last active
/// administrator (ACC-R12). Idempotent: an already inactive user is returned unchanged, without a
/// new event. The person party stays active (it remains in the directory).
/// <para>
/// Scope: the contract declares <c>x-scope: none</c>. Deactivating a user with no role
/// assignment stays unscoped. For a user who holds assignments, the caller's
/// <c>USER:UPDATE</c> must cover every scope of them (<see cref="UserScopeGuard"/>), 403
/// otherwise: deactivation is effectively irreversible (no reactivate endpoint, and the
/// <c>oid</c> stays taken), so an admin of one legal entity must not be able to lock out the
/// admin of another, or a global administrator.
/// </para>
/// <para>
/// Order of the checks: 404 / 409 (<c>If-Match</c>) → 403 (scope) → ORG-R6 (SYSTEM) → ACC-R13
/// (self) → idempotent no-op (already inactive: returned unchanged, without error) → ACC-R12
/// (not the last qualifying <c>SYSTEM_ADMIN</c> administrator, counted under the lock of
/// <see cref="LastSystemAdministratorGuard"/>) → deactivate.
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
    private readonly ISystemAdministratorReadDb _systemAdministrators;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public DeactivateUserHandler(
        IRequestTransaction transaction,
        IAppUserRepository users,
        IPartyRepository parties,
        IUserReadDb readDb,
        IRoleAssignmentScopeChecker scopeChecker,
        ISystemAdministratorReadDb systemAdministrators,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _transaction = transaction;
        _users = users;
        _parties = parties;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
        _systemAdministrators = systemAdministrators;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
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

        // BE-R1: a unit of work only begins for an authenticated caller with a resolved user.
        var actor = _currentUser.UserId!.Value;

        // ORG-R6, then ACC-R13 - without changing anything yet.
        var allowed = user.EnsureCanBeDeactivatedBy(actor);
        if (allowed.IsFailure)
        {
            return Result.Failure<UserDetails>(allowed.Error!);
        }

        if (user.IsActive)
        {
            var keepsOne = await LastSystemAdministratorGuard.EnsureDeactivationKeepsOneAsync(
                _systemAdministrators, user.Id, _timeProvider.GetUtcNow(), cancellationToken);
            if (keepsOne.IsFailure)
            {
                return Result.Failure<UserDetails>(keepsOne.Error!);
            }
        }

        var deactivated = user.Deactivate(actor);
        if (deactivated.IsFailure)
        {
            return Result.Failure<UserDetails>(deactivated.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(UserDetails.From(user, _users.GetMetadata(user), party, _parties.GetMetadata(party)));
    }
}
