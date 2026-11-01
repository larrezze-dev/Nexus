using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.RoleAssignments.RevokeRoleAssignment;

/// <summary>
/// <c>POST /v1/role-assignments/{id}/revoke</c> (<c>revokeRoleAssignment</c>), guarded by
/// <c>If-Match</c>. Sets <c>validTo = now()</c>/<c>revokedBy</c> (ACC-R3): never deletes. ACC-R5
/// and ACC-R6 are enforced by <see cref="Domain.RoleAssignments.RoleAssignment.Revoke"/> itself.
/// <para>
/// Scope: the contract declares <c>x-scope: none</c>, so the permission filter only checks that
/// the caller holds <c>ROLE_ASSIGNMENT:UPDATE</c> <i>somewhere</i>, and <c>access</c> has no RLS
/// to narrow which rows they reach. Without more, an <c>ORG_ADMIN</c> of one legal entity could
/// revoke another legal entity's admin, or a second GLOBAL administrator. So, once the row is
/// loaded and before anything else, this handler checks that the caller's
/// <c>ROLE_ASSIGNMENT:UPDATE</c> <b>covers the row's own scope</b> - the same covering rule as
/// ACC-R4 on create, applied as ACC-R10 asks of write endpoints ("pass the row's scope"), through
/// <see cref="IRoleAssignmentScopeChecker"/> (the same resolver the create filter uses) - and
/// answers 403 otherwise. The contract itself is unchanged.
/// </para>
/// </summary>
public sealed record RevokeRoleAssignmentCommand(Guid Id, uint ExpectedVersion) : ICommand<RoleAssignmentDetails>;

internal sealed class RevokeRoleAssignmentHandler : ICommandHandler<RevokeRoleAssignmentCommand, RoleAssignmentDetails>
{
    private const string RevokePermission = "ROLE_ASSIGNMENT:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IRoleAssignmentRepository _assignments;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public RevokeRoleAssignmentHandler(
        IRequestTransaction transaction,
        IRoleAssignmentRepository assignments,
        IRoleAssignmentScopeChecker scopeChecker,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _transaction = transaction;
        _assignments = assignments;
        _scopeChecker = scopeChecker;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<Result<RoleAssignmentDetails>> Handle(RevokeRoleAssignmentCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await VersionedLoader.LoadAsync(
            _assignments.GetByIdAsync,
            _assignments.GetMetadata,
            command.Id,
            command.ExpectedVersion,
            RoleAssignmentErrors.NotFound(command.Id),
            RoleAssignmentErrors.StaleVersion,
            cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<RoleAssignmentDetails>(loaded.Error!);
        }

        var assignment = loaded.Value;

        // Before ACC-R5/ACC-R6 and before the idempotent no-op: a caller whose scope does not
        // cover the row learns nothing about it beyond "forbidden".
        if (!await _scopeChecker.HasPermissionCoveringAsync(
                RevokePermission, assignment.ScopeType, assignment.ScopeEntityId, cancellationToken))
        {
            return Result.Failure<RoleAssignmentDetails>(RoleAssignmentErrors.RevokeScopeNotCovered);
        }

        var revokeResult = assignment.Revoke(_currentUser.UserId!.Value, _timeProvider.GetUtcNow());
        if (revokeResult.IsFailure)
        {
            return Result.Failure<RoleAssignmentDetails>(revokeResult.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(RoleAssignmentDetails.From(assignment, _assignments.GetMetadata(assignment)));
    }
}
