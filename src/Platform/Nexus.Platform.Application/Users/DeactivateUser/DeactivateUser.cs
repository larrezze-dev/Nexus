using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Parties;

namespace Nexus.Platform.Application.Users.DeactivateUser;

/// <summary>
/// <c>POST /v1/users/{id}/deactivate</c> (<c>deactivateUser</c>), guarded by <c>If-Match</c>.
/// Never for SYSTEM (ORG-R6). Idempotent: an already inactive user is returned unchanged,
/// without a new event. The person party stays active (it remains in the directory).
/// <para>
/// Scope: the contract declares <c>x-scope: none</c> and deactivating an ordinary user stays
/// unscoped. Deactivating a user who currently holds an active <c>GLOBAL</c> role assignment is
/// different: any <c>USER:UPDATE</c> holder (ORG_ADMIN/HR_ADMIN of one legal entity) could
/// otherwise lock every global administrator out. For such a target the caller's
/// <c>USER:UPDATE</c> must itself be held at <c>GLOBAL</c> scope (same mechanism as the
/// <c>oid</c> change in <c>updateUser</c>), 403 otherwise.
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

        // Before any change (and before ORG-R6): a global administrator can only be deactivated
        // by a caller whose USER:UPDATE is GLOBAL. An already inactive user has no active
        // assignment, so the idempotent no-op below is unaffected.
        if (await _readDb.HasActiveGlobalAssignmentAsync(user.Id, cancellationToken)
            && !await _scopeChecker.HasPermissionCoveringAsync(UpdatePermission, "GLOBAL", null, cancellationToken))
        {
            return Result.Failure<UserDetails>(UserErrors.DeactivateGlobalUserRequiresGlobal);
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
