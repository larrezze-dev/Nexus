using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Application.Roles.UpdateRolePermissions;

/// <summary>
/// <c>PUT /v1/roles/{id}/permissions</c> (<c>updateRolePermissions</c>, ACC-R7): the client sends
/// the full desired set of permission codes; the server diffs it against the role's
/// currently-valid grants and closes (<c>valid_to</c>) what is no longer wanted while opening a
/// new row for what is newly wanted. Codes already granted and still wanted are left untouched
/// (no new row, no close) - this is a set replacement, not a bulk revoke-then-grant.
/// <para>
/// Guarded by <c>If-Match</c> with the role's <c>ETag</c> (<see cref="UpdateRolePermissionsCommand.ExpectedVersion"/>,
/// the role row's <c>xmin</c>; domain/access.md §8, decided 2026-11-11): missing → 428 (endpoint),
/// different → 409 <c>CONCURRENCY</c>. Order of the checks: 404 → 409 (<c>If-Match</c>) → 400
/// (unknown permission code) → 422 ACC-R11 (<see cref="Role.EnsureKeepsCriticalPermissions"/>:
/// <c>SYSTEM_ADMIN</c> keeps its five critical permissions) → diff and write.
/// </para>
/// <para>
/// When the set actually changes, <c>Role.RaisePermissionsChanged</c> raises <c>RoleUpdated</c>
/// (with the new permission codes), which bumps the role's persisted <c>aggregate_version</c>
/// (ADR-0019): the role's own row is UPDATEd, so its
/// <c>xmin</c> (ETag) and <c>updated_at</c>/<c>updated_by</c> change too, and two concurrent
/// permission edits on the same role conflict - the loser gets 409 (concurrency). A request that
/// changes nothing leaves the role row untouched. Either way the response carries the role's
/// <c>ETag</c> after the commit (<see cref="RolePermissionsUpdate.Version"/>): the new
/// <c>xmin</c> EF Core read back from the <c>UPDATE</c>, or the unchanged one, so the client can
/// chain the next <c>PUT</c> with it.
/// </para>
/// <para>
/// The 200 response (the role's new current grants) is built entirely from data already read
/// before <see cref="IRequestTransaction.CommitAsync"/>: the unit of work is single-use and
/// releases its connection on commit (conventions-backend.md §4), so nothing here reads the
/// database again afterwards.
/// </para>
/// </summary>
public sealed record UpdateRolePermissionsCommand(Guid RoleId, uint ExpectedVersion, IReadOnlyList<string> PermissionCodes)
    : ICommand<RolePermissionsUpdate>;

/// <summary>The role's current grants after the replacement, and the role's version (<c>ETag</c>) after the commit.</summary>
public sealed record RolePermissionsUpdate(IReadOnlyList<PermissionGrantItem> Grants, uint Version);

internal sealed class UpdateRolePermissionsValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsValidator()
    {
        RuleFor(c => c.PermissionCodes).NotNull();
    }
}

internal sealed class UpdateRolePermissionsHandler : ICommandHandler<UpdateRolePermissionsCommand, RolePermissionsUpdate>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissionWrites;
    private readonly IRolePermissionReadDb _rolePermissionReads;
    private readonly IIdGenerator _ids;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public UpdateRolePermissionsHandler(
        IRequestTransaction transaction,
        IRoleRepository roles,
        IRolePermissionRepository rolePermissionWrites,
        IRolePermissionReadDb rolePermissionReads,
        IIdGenerator ids,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _transaction = transaction;
        _roles = roles;
        _rolePermissionWrites = rolePermissionWrites;
        _rolePermissionReads = rolePermissionReads;
        _ids = ids;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<Result<RolePermissionsUpdate>> Handle(
        UpdateRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await VersionedLoader.LoadAsync(
            _roles.GetByIdAsync,
            _roles.GetMetadata,
            command.RoleId,
            command.ExpectedVersion,
            RoleErrors.NotFound(command.RoleId),
            RoleErrors.StaleVersion,
            cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<RolePermissionsUpdate>(loaded.Error!);
        }

        var role = loaded.Value;
        var desiredCodes = command.PermissionCodes.Distinct(StringComparer.Ordinal).ToList();

        var catalog = await _rolePermissionReads.ResolveAsync(desiredCodes, cancellationToken);
        var unknownCode = desiredCodes.FirstOrDefault(code => !catalog.ContainsKey(code));
        if (unknownCode is not null)
        {
            return Result.Failure<RolePermissionsUpdate>(RoleErrors.UnknownPermissionCode(unknownCode));
        }

        // ACC-R11: SYSTEM_ADMIN keeps its critical permissions (an invariant of the resulting set).
        var keepsCritical = role.EnsureKeepsCriticalPermissions(desiredCodes);
        if (keepsCritical.IsFailure)
        {
            return Result.Failure<RolePermissionsUpdate>(keepsCritical.Error!);
        }

        var currentGrants = await _rolePermissionReads.GetCurrentGrantsAsync(command.RoleId, cancellationToken);

        var toRemoveCodes = currentGrants.Keys.Except(desiredCodes, StringComparer.Ordinal).ToList();
        var toAddCodes = desiredCodes.Except(currentGrants.Keys, StringComparer.Ordinal).ToList();
        var now = _timeProvider.GetUtcNow();

        if (toRemoveCodes.Count > 0 || toAddCodes.Count > 0)
        {
            // BE-R1: a unit of work only begins for an authenticated caller with a resolved user.
            var actor = _currentUser.UserId!.Value;

            if (toRemoveCodes.Count > 0)
            {
                var toRemoveIds = toRemoveCodes.Select(code => currentGrants[code].PermissionId).ToList();
                var open = await _rolePermissionWrites.GetOpenAsync(command.RoleId, toRemoveIds, cancellationToken);
                foreach (var grant in open)
                {
                    grant.Close(now);
                }
            }

            foreach (var code in toAddCodes)
            {
                _rolePermissionWrites.Add(RolePermission.Open(_ids.NewId(), command.RoleId, catalog[code].Id, now, actor));
            }

            role.RaisePermissionsChanged(desiredCodes.OrderBy(code => code, StringComparer.Ordinal).ToList());
        }

        // Built now, from data already in hand: the unit of work's connection is gone after commit.
        var response = desiredCodes
            .Select(code =>
            {
                var item = catalog[code];
                var validFrom = toAddCodes.Contains(code, StringComparer.Ordinal) ? now : currentGrants[code].ValidFrom;
                return new PermissionGrantItem(item.Code, item.ResourceType, item.Action, item.CaptionI18n, validFrom.UtcDateTime, null);
            })
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ToList();

        await _transaction.CommitAsync(cancellationToken);

        // The role's version is read from EF Core's change tracker (refreshed by the UPDATE's
        // RETURNING xmin when the role row was written), not from the database.
        return Result.Success(new RolePermissionsUpdate(response, _roles.GetMetadata(role).Version));
    }
}
