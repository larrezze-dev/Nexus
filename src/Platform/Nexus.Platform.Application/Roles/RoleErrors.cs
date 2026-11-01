using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Roles;

/// <summary>
/// Caller-facing errors of the role use cases that are not domain invariants. The database
/// constraints they correspond to are registered with the web layer
/// (<c>AddNexusConstraintViolation</c>) using these same errors, so a violation detected by
/// PostgreSQL reads exactly like one detected in code.
/// </summary>
public static class RoleErrors
{
    /// <summary>UNIQUE on <c>access.role.code</c>.</summary>
    public const string CodeUniqueConstraint = "role_code_key";

    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string RolePrimaryKey = "role_pkey";

    /// <summary>
    /// <c>EXCLUDE USING gist (role_id WITH =, permission_id WITH =, tstzrange(valid_from,
    /// coalesce(valid_to,'infinity')) WITH &amp;&amp;)</c> on <c>access.role_permission</c>. Unnamed in
    /// the DDL; this is PostgreSQL's generated name (pinned by a catalog test).
    /// </summary>
    public const string RolePermissionOverlapExclusion = "role_permission_role_id_permission_id_tstzrange_excl";

    public static readonly Error CodeAlreadyUsed =
        Error.Conflict("Another role already has this code.");

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A role with this id already exists with different content.");

    /// <summary>
    /// <c>PUT /v1/roles/{id}/permissions</c> tried to open a grant whose validity window overlaps
    /// another grant of the same permission on the same role that it did not see as current -
    /// in practice, two concurrent updates adding the same code. The caller re-reads and retries.
    /// </summary>
    public static readonly Error PermissionGrantOverlap =
        Error.Concurrency("The role's permissions were changed concurrently by another request; reload them and retry.");

    public static Error NotFound(Guid id) => Error.NotFound($"Role '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The role has changed since the ETag sent in If-Match was obtained.");

    /// <summary>
    /// <c>PUT /v1/roles/{id}/permissions</c>: a requested <c>permissionCodes</c> entry does not
    /// exist in the <c>access.permission</c> catalog.
    /// </summary>
    public static Error UnknownPermissionCode(string code) =>
        Error.Validation($"'{code}' is not a known permission code.");
}
