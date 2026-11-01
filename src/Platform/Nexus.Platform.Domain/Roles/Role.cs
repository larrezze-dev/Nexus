using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Roles;

/// <summary>
/// <c>access.role</c> (domain/access.md §3): a named bundle of permissions
/// (<see cref="Nexus.Platform.Domain.Roles.RolePermission"/>) that can be assigned to a user in
/// a scope. <c>code</c> and <see cref="IsSystem"/> are immutable after creation
/// (<c>UpdateRoleRequest</c> never carries them).
/// </summary>
public sealed class Role : AggregateRoot<Guid>
{
    private Role(Guid id)
        : base(id)
    {
    }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string> CaptionI18n { get; private set; } = new Dictionary<string, string>();

    public string? Description { get; private set; }

    /// <summary>Always <see langword="false"/> for a role created through the API; the 11 seeded roles are <see langword="true"/>.</summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Creates a new, non-system, active role and raises <see cref="RoleUpdated"/> with no
    /// permissions yet (EV-R4 full state).
    /// </summary>
    public static Result<Role> Create(Guid id, string code, IReadOnlyDictionary<string, string> captionI18n, string? description)
    {
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.Null(captionI18n, nameof(captionI18n));

        var role = new Role(id)
        {
            Code = code,
            CaptionI18n = captionI18n,
            Description = description,
            IsSystem = false,
            IsActive = true,
        };

        role.RaiseEvent(new RoleUpdated(
            role.Id, role.Code, role.CaptionI18n, role.Description, role.IsSystem, role.IsActive, PermissionCodes: []));

        return Result.Success(role);
    }

    /// <summary>
    /// Replaces <see cref="CaptionI18n"/> and <see cref="Description"/> (PUT semantics).
    /// <paramref name="currentPermissionCodes"/> is this role's own currently-valid permission
    /// codes (<c>access.role_permission</c>, read by the calling handler: the aggregate has no
    /// persistence access), carried unchanged into <see cref="RoleUpdated"/>'s full-state
    /// payload. Returns whether anything changed; raises the event only in that case.
    /// </summary>
    public bool Update(IReadOnlyDictionary<string, string> captionI18n, string? description, IReadOnlyList<string> currentPermissionCodes)
    {
        Guard.Against.Null(captionI18n, nameof(captionI18n));

        var changed = !CaptionEquals(captionI18n) || !string.Equals(Description, description, StringComparison.Ordinal);
        if (!changed)
        {
            return false;
        }

        CaptionI18n = captionI18n;
        Description = description;

        RaiseEvent(new RoleUpdated(Id, Code, CaptionI18n, Description, IsSystem, IsActive, currentPermissionCodes));
        return true;
    }

    /// <summary>
    /// Deactivates the role (no DELETE in master data). Rule ACC-R9: a system role (one of the
    /// 11 seeded roles) is never deactivated. Idempotent: deactivating an already inactive role
    /// changes nothing and raises nothing.
    /// </summary>
    public Result<bool> Deactivate(IReadOnlyList<string> currentPermissionCodes)
    {
        if (IsSystem)
        {
            return Result.Failure<bool>(new Error("ACC-R9", "A role with isSystem = true cannot be deactivated."));
        }

        if (!IsActive)
        {
            return Result.Success(false);
        }

        IsActive = false;
        RaiseEvent(new RoleUpdated(Id, Code, CaptionI18n, Description, IsSystem, IsActive, currentPermissionCodes));
        return Result.Success(true);
    }

    /// <summary>
    /// Rule ACC-R7: changing a role's permissions does not touch any of this aggregate's own
    /// columns, but still raises <see cref="RoleUpdated"/> (domain/access.md §9) carrying the
    /// new full set of currently-valid permission codes. Called by
    /// <c>UpdateRolePermissions</c> only when the diff actually adds or closes at least one row.
    /// </summary>
    public void RaisePermissionsChanged(IReadOnlyList<string> newPermissionCodes) =>
        RaiseEvent(new RoleUpdated(Id, Code, CaptionI18n, Description, IsSystem, IsActive, newPermissionCodes));

    private bool CaptionEquals(IReadOnlyDictionary<string, string> other) =>
        CaptionI18n.Count == other.Count
        && CaptionI18n.All(pair => other.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
}
