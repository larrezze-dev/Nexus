using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.Roles.DeactivateRole;

/// <summary>
/// <c>POST /v1/roles/{id}/deactivate</c> (<c>deactivateRole</c>), guarded by <c>If-Match</c>.
/// Rejects a system role with 422 <c>ACC-R9</c> (<see cref="Nexus.Platform.Domain.Roles.Role.Deactivate"/>).
/// Idempotent: an already inactive role is returned unchanged, without a new event.
/// </summary>
public sealed record DeactivateRoleCommand(Guid Id, uint ExpectedVersion) : ICommand<RoleDetails>;

internal sealed class DeactivateRoleHandler : ICommandHandler<DeactivateRoleCommand, RoleDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionReadDb _rolePermissions;

    public DeactivateRoleHandler(IRequestTransaction transaction, IRoleRepository roles, IRolePermissionReadDb rolePermissions)
    {
        _transaction = transaction;
        _roles = roles;
        _rolePermissions = rolePermissions;
    }

    public async Task<Result<RoleDetails>> Handle(DeactivateRoleCommand command, CancellationToken cancellationToken)
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
        var deactivated = role.Deactivate(currentGrants.Keys.ToList());
        if (deactivated.IsFailure)
        {
            return Result.Failure<RoleDetails>(deactivated.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(RoleDetails.From(role, _roles.GetMetadata(role)));
    }
}
