namespace Nexus.Platform.Domain.Roles;

/// <summary>
/// The seeded <c>SYSTEM_ADMIN</c> role and the invariants that hang on it (domain/access.md §6).
/// Identified by its <see cref="RoleCode"/>, not by <see cref="Role.IsSystem"/>: all 11 seeded
/// roles are <c>is_system = true</c>, and <c>access.role.code</c> is unique and immutable after
/// creation, so the code is what singles this role out.
/// <list type="bullet">
///   <item>ACC-R11: the role always keeps <see cref="CriticalPermissionCodes"/>
///   (<see cref="Role.EnsureKeepsCriticalPermissions"/>).</item>
///   <item>ACC-R12: at least one active user other than SYSTEM always holds a currently valid
///   <c>GLOBAL</c> assignment of this role (enforced by the revoke/deactivate use cases, under a
///   lock on this role's row: the check needs other aggregates).</item>
/// </list>
/// </summary>
public static class SystemAdministrator
{
    /// <summary><c>access.role.code</c> of the seeded system administrator role.</summary>
    public const string RoleCode = "SYSTEM_ADMIN";

    /// <summary>
    /// ACC-R11: the permissions without which nobody could administer roles and assignments any
    /// more (read and edit roles, read, grant and revoke assignments). Ordinal codes.
    /// </summary>
    public static readonly IReadOnlyList<string> CriticalPermissionCodes =
    [
        "ROLE:READ",
        "ROLE:UPDATE",
        "ROLE_ASSIGNMENT:READ",
        "ROLE_ASSIGNMENT:CREATE",
        "ROLE_ASSIGNMENT:UPDATE",
    ];
}
