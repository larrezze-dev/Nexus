using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.RoleAssignments.RevokeRoleAssignment;

/// <summary>
/// <c>POST /v1/role-assignments/{id}/revoke</c> (<c>revokeRoleAssignment</c>), guarded by
/// <c>If-Match</c>. Sets <c>validTo = now()</c>/<c>revokedBy</c> (ACC-R3): never deletes. ACC-R5
/// and ACC-R6 are enforced by <see cref="Domain.RoleAssignments.RoleAssignment.Revoke"/> itself -
/// unlike <c>createRoleAssignment</c>, this endpoint has <c>x-scope: none</c>, so there is no
/// scope-covering check in the permission filter for it (domain/access.md §4): this handler is
/// the only enforcement point here.
/// </summary>
public sealed record RevokeRoleAssignmentCommand(Guid Id, uint ExpectedVersion) : ICommand<RoleAssignmentDetails>;

internal sealed class RevokeRoleAssignmentHandler : ICommandHandler<RevokeRoleAssignmentCommand, RoleAssignmentDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleAssignmentRepository _assignments;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public RevokeRoleAssignmentHandler(
        IRequestTransaction transaction,
        IRoleAssignmentRepository assignments,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _transaction = transaction;
        _assignments = assignments;
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
        var revokeResult = assignment.Revoke(_currentUser.UserId!.Value, _timeProvider.GetUtcNow());
        if (revokeResult.IsFailure)
        {
            return Result.Failure<RoleAssignmentDetails>(revokeResult.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(RoleAssignmentDetails.From(assignment, _assignments.GetMetadata(assignment)));
    }
}
