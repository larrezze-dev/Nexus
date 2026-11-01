using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Validation;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Application.Roles.CreateRole;

/// <summary>
/// <c>POST /v1/roles</c> (<c>createRole</c>): idempotent create (client-supplied UUIDv7
/// <see cref="Id"/>). Always creates a non-system role (<c>Role.Create</c> hardcodes
/// <c>isSystem = false</c>): the 11 seeded roles are inserted by <c>db/seed/common</c>, never
/// through this endpoint.
/// </summary>
public sealed record CreateRoleCommand(
    Guid? Id,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? Description) : ICommand<CreateRoleResult>;

/// <summary>The role and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateRoleResult(RoleDetails Role, bool Created);

internal sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.Code).MustBeNonBlank();
        RuleFor(c => c.CaptionI18n).MustHaveGermanCaption();
    }
}

internal sealed class CreateRoleHandler : ICommandHandler<CreateRoleCommand, CreateRoleResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleRepository _roles;
    private readonly IIdGenerator _ids;

    public CreateRoleHandler(IRequestTransaction transaction, IRoleRepository roles, IIdGenerator ids)
    {
        _transaction = transaction;
        _roles = roles;
        _ids = ids;
    }

    public async Task<Result<CreateRoleResult>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _roles.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        var roleResult = Role.Create(command.Id ?? _ids.NewId(), command.Code, command.CaptionI18n, command.Description);
        if (roleResult.IsFailure)
        {
            return Result.Failure<CreateRoleResult>(roleResult.Error!);
        }

        var role = roleResult.Value;
        _roles.Add(role);

        // The code's uniqueness is enforced by the database at this point; its violation is
        // translated by the web layer (RoleErrors, AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateRoleResult(RoleDetails.From(role, _roles.GetMetadata(role)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateRoleResult>> ReplayAsync(CreateRoleCommand command, Role existing, CancellationToken cancellationToken)
    {
        var identical =
            string.Equals(existing.Code, command.Code, StringComparison.Ordinal)
            && CaptionEquals(existing.CaptionI18n, command.CaptionI18n)
            && string.Equals(existing.Description, command.Description, StringComparison.Ordinal);

        if (!identical)
        {
            return Result.Failure<CreateRoleResult>(RoleErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateRoleResult(RoleDetails.From(existing, _roles.GetMetadata(existing)), Created: false));
    }

    private static bool CaptionEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
}
