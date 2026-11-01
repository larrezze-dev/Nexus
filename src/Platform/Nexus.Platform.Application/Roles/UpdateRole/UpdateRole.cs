using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Validation;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.Roles.UpdateRole;

/// <summary>
/// <c>PUT /v1/roles/{id}</c> (<c>updateRole</c>), full replacement guarded by <c>If-Match</c>
/// (<see cref="ExpectedVersion"/>). Only <c>captionI18n</c>/<c>description</c>: <c>code</c> and
/// <c>isSystem</c> are immutable after creation (not part of <c>UpdateRoleRequest</c>).
/// </summary>
public sealed record UpdateRoleCommand(Guid Id, uint ExpectedVersion, IReadOnlyDictionary<string, string> CaptionI18n, string? Description)
    : ICommand<RoleDetails>;

internal sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(c => c.CaptionI18n).MustHaveGermanCaption();
    }
}

internal sealed class UpdateRoleHandler : ICommandHandler<UpdateRoleCommand, RoleDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionReadDb _rolePermissions;

    public UpdateRoleHandler(IRequestTransaction transaction, IRoleRepository roles, IRolePermissionReadDb rolePermissions)
    {
        _transaction = transaction;
        _roles = roles;
        _rolePermissions = rolePermissions;
    }

    public async Task<Result<RoleDetails>> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await VersionedLoader.LoadAsync(
            _roles.GetByIdAsync,
            _roles.GetMetadata,
            command.Id,
            command.ExpectedVersion,
            RoleErrors.NotFound(command.Id),
            RoleErrors.StaleVersion,
            cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<RoleDetails>(loaded.Error!);
        }

        var role = loaded.Value;
        var currentGrants = await _rolePermissions.GetCurrentGrantsAsync(role.Id, cancellationToken);
        role.Update(command.CaptionI18n, command.Description, currentGrants.Keys.ToList());

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(RoleDetails.From(role, _roles.GetMetadata(role)));
    }
}
