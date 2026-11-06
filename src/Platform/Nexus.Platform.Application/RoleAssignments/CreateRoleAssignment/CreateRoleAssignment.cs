using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Validation;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.RoleAssignments;

namespace Nexus.Platform.Application.RoleAssignments.CreateRoleAssignment;

/// <summary>
/// <c>POST /v1/role-assignments</c> (<c>createRoleAssignment</c>): idempotent create (client-
/// supplied UUIDv7 <see cref="Id"/>). ACC-R4 (the granter's own scope must cover the target
/// scope) is already enforced by <c>AccessPermissionChecker</c> on the endpoint's permission
/// filter (<c>x-scope: body.scopeEntityId</c>) before this handler ever runs; ACC-R1/ACC-R5 and
/// ACC-R2's "PROJECT is rejected before step 2.1" are enforced by
/// <see cref="RoleAssignment.Create"/> itself; ACC-R2's "scopeEntityId exists in the table
/// scopeType names" needs a database lookup and is enforced by the handler
/// (<see cref="IRoleAssignmentReadDb.ScopeEntityExistsAsync"/>). A <c>validTo</c> that is not in
/// the future is rejected by the handler too (time-dependent, so not in the pure validator).
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
    public CreateRoleAssignmentValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.UserId).Must(id => id != Guid.Empty).WithMessage("'userId' must not be an empty UUID.");
        RuleFor(c => c.RoleId).Must(id => id != Guid.Empty).WithMessage("'roleId' must not be an empty UUID.");
        // MustBeOneOf, not an opaque .Must(...), so the generated OpenAPI document carries the
        // same `enum` the contract declares (ADR-0014).
        RuleFor(c => c.ScopeType)
            .NotNull()
            .MustBeOneOf(RoleAssignment.ScopeTypes)
            .WithMessage("'scopeType' is not a recognized scope type.");
        RuleFor(c => c.ScopeEntityId).Must(id => id is null || id != Guid.Empty).WithMessage("'scopeEntityId' must not be an empty UUID.");
        // ADR-0016: both instants end up in platform.role-assignment.changed (Avro
        // timestamp-micros); a finer value would fail the outbox write (500), and PostgreSQL's
        // rounding would make the stored value differ from the event and break the replay check.
        RuleFor(c => c.ValidFrom).MustBeMicrosecondPrecision().WithName("validFrom");
        RuleFor(c => c.ValidTo).MustBeMicrosecondPrecision().WithName("validTo");
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
    private readonly IRoleAssignmentReadDb _reads;
    private readonly ICurrentUser _currentUser;
    private readonly IIdGenerator _ids;
    private readonly TimeProvider _timeProvider;

    public CreateRoleAssignmentHandler(
        IRequestTransaction transaction,
        IRoleAssignmentRepository assignments,
        IRoleAssignmentReadDb reads,
        ICurrentUser currentUser,
        IIdGenerator ids,
        TimeProvider timeProvider)
    {
        _transaction = transaction;
        _assignments = assignments;
        _reads = reads;
        _currentUser = currentUser;
        _ids = ids;
        _timeProvider = timeProvider;
    }

    public async Task<Result<CreateRoleAssignmentResult>> Handle(CreateRoleAssignmentCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _assignments.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        // The DDL's CHECK (valid_to IS NULL OR valid_to >= created_at): a validTo that is not
        // in the future would otherwise surface as a raw CHECK violation (500).
        if (command.ValidTo is { } validTo && validTo <= _timeProvider.GetUtcNow())
        {
            return Result.Failure<CreateRoleAssignmentResult>(RoleAssignmentErrors.ValidToInPast);
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

        // ACC-R2: Create already rejected PROJECT and checked ACC-R1, so a non-null
        // scopeEntityId here belongs to GROUP/LEGAL_ENTITY/BUSINESS_UNIT/WAREHOUSE.
        if (assignment.ScopeEntityId is { } scopeEntityId
            && !await _reads.ScopeEntityExistsAsync(assignment.ScopeType, scopeEntityId, cancellationToken))
        {
            return Result.Failure<CreateRoleAssignmentResult>(RoleAssignmentErrors.ScopeEntityNotFound(assignment.ScopeType));
        }

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
