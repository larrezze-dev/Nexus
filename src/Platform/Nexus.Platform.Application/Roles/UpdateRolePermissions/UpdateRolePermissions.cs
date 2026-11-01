using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Application.Roles.UpdateRolePermissions;

/// <summary>
/// <c>PUT /v1/roles/{id}/permissions</c> (<c>updateRolePermissions</c>, ACC-R7): the client sends
/// the full desired set of permission codes; the server diffs it against the role's
/// currently-valid grants and closes (<c>valid_to</c>) what is no longer wanted while opening a
/// new row for what is newly wanted. Codes already granted and still wanted are left untouched
/// (no new row, no close) - this is a set replacement, not a bulk revoke-then-grant. No
/// <c>If-Match</c> (the contract's <c>updateRolePermissions</c> has none): this endpoint never
/// touches the role's own row/version.
/// <para>
/// The 200 response (the role's new current grants) is built entirely from data already read
/// before <see cref="IRequestTransaction.CommitAsync"/>: the unit of work is single-use and
/// releases its connection on commit (conventions-backend.md §4), so nothing here reads the
/// database again afterwards.
/// </para>
/// </summary>
public sealed record UpdateRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> PermissionCodes)
    : ICommand<IReadOnlyList<PermissionGrantItem>>;

internal sealed class UpdateRolePermissionsValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsValidator()
    {
        RuleFor(c => c.PermissionCodes).NotNull();
    }
}

internal sealed class UpdateRolePermissionsHandler : ICommandHandler<UpdateRolePermissionsCommand, IReadOnlyList<PermissionGrantItem>>
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

    public async Task<Result<IReadOnlyList<PermissionGrantItem>>> Handle(
        UpdateRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var role = await _roles.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<IReadOnlyList<PermissionGrantItem>>(RoleErrors.NotFound(command.RoleId));
        }

        var desiredCodes = command.PermissionCodes.Distinct(StringComparer.Ordinal).ToList();

        var catalog = await _rolePermissionReads.ResolveAsync(desiredCodes, cancellationToken);
        var unknownCode = desiredCodes.FirstOrDefault(code => !catalog.ContainsKey(code));
        if (unknownCode is not null)
        {
            return Result.Failure<IReadOnlyList<PermissionGrantItem>>(RoleErrors.UnknownPermissionCode(unknownCode));
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

        return Result.Success<IReadOnlyList<PermissionGrantItem>>(response);
    }
}
