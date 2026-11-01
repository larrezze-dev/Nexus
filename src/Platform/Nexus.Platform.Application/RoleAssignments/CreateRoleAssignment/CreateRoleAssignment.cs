using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.RoleAssignments;

namespace Nexus.Platform.Application.RoleAssignments.CreateRoleAssignment;

/// <summary>
/// <c>POST /v1/role-assignments</c> (<c>createRoleAssignment</c>): idempotent create (client-
/// supplied UUIDv7 <see cref="Id"/>). ACC-R4 (the granter's own scope must cover the target
/// scope) is already enforced by <c>AccessPermissionChecker</c> on the endpoint's permission
/// filter (<c>x-scope: body.scopeEntityId</c>) before this handler ever runs; ACC-R1/ACC-R2/
/// ACC-R5 are enforced by <see cref="RoleAssignment.Create"/> itself.
/// </summary>
public sealed record CreateRoleAssignmentCommand(
    Guid? Id,
    Guid UserId,
    Guid RoleId,
    string ScopeType,
    Guid? ScopeEntityId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo) : ICommand<CreateRoleAssignmentResult>;

/// <summary>The role assignment and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateRoleAssignmentResult(RoleAssignmentDetails Assignment, bool Created);

internal sealed class CreateRoleAssignmentValidator : AbstractValidator<CreateRoleAssignmentCommand>
{
    private static readonly IReadOnlySet<string> AllowedScopeTypes = new HashSet<string>(
        [
            RoleAssignment.ScopeGroup, RoleAssignment.ScopeLegalEntity, RoleAssignment.ScopeBusinessUnit,
            RoleAssignment.ScopeWarehouse, RoleAssignment.ScopeProject, RoleAssignment.ScopeGlobal,
        ],
        StringComparer.Ordinal);

    public CreateRoleAssignmentValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.UserId).Must(id => id != Guid.Empty).WithMessage("'userId' must not be an empty UUID.");
        RuleFor(c => c.RoleId).Must(id => id != Guid.Empty).WithMessage("'roleId' must not be an empty UUID.");
        RuleFor(c => c.ScopeType).Must(AllowedScopeTypes.Contains).WithMessage("'scopeType' is not a recognized scope type.");
        RuleFor(c => c.ScopeEntityId).Must(id => id is null || id != Guid.Empty).WithMessage("'scopeEntityId' must not be an empty UUID.");
        RuleFor(c => c)
            .Must(c => c.ValidTo is null || c.ValidTo > c.ValidFrom)
            .WithMessage("'validTo' must be after 'validFrom'.")
            .WithName("validTo");
    }
}

internal sealed class CreateRoleAssignmentHandler : ICommandHandler<CreateRoleAssignmentCommand, CreateRoleAssignmentResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleAssignmentRepository _assignments;
    private readonly ICurrentUser _currentUser;
    private readonly IIdGenerator _ids;

    public CreateRoleAssignmentHandler(
        IRequestTransaction transaction,
        IRoleAssignmentRepository assignments,
        ICurrentUser currentUser,
        IIdGenerator ids)
    {
        _transaction = transaction;
        _assignments = assignments;
        _currentUser = currentUser;
        _ids = ids;
    }

    public async Task<Result<CreateRoleAssignmentResult>> Handle(CreateRoleAssignmentCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _assignments.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        var assignmentResult = RoleAssignment.Create(
            command.Id ?? _ids.NewId(),
            command.UserId,
            command.RoleId,
            command.ScopeType,
            command.ScopeEntityId,
            command.ValidFrom,
            command.ValidTo,
            grantedBy: _currentUser.UserId!.Value);
        if (assignmentResult.IsFailure)
        {
            return Result.Failure<CreateRoleAssignmentResult>(assignmentResult.Error!);
        }

        var assignment = assignmentResult.Value;
        _assignments.Add(assignment);

        // userId/roleId existence is enforced by the database at this point; their violations
        // are translated by the web layer (RoleAssignmentErrors, AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateRoleAssignmentResult(
            RoleAssignmentDetails.From(assignment, _assignments.GetMetadata(assignment)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateRoleAssignmentResult>> ReplayAsync(
        CreateRoleAssignmentCommand command, RoleAssignment existing, CancellationToken cancellationToken)
    {
        var identical =
            existing.UserId == command.UserId
            && existing.RoleId == command.RoleId
            && string.Equals(existing.ScopeType, command.ScopeType, StringComparison.Ordinal)
            && existing.ScopeEntityId == command.ScopeEntityId
            && existing.ValidFrom == command.ValidFrom
            && existing.ValidTo == command.ValidTo;

        if (!identical)
        {
            return Result.Failure<CreateRoleAssignmentResult>(RoleAssignmentErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateRoleAssignmentResult(
            RoleAssignmentDetails.From(existing, _assignments.GetMetadata(existing)), Created: false));
    }
}
