using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.RoleAssignments;

/// <summary>
/// Caller-facing errors of the role-assignment use cases that are not domain invariants (those
/// live on <c>RoleAssignment</c> itself: ACC-R1/ACC-R2/ACC-R5/ACC-R6). The database constraints
/// below are registered with the web layer (<c>AddNexusConstraintViolation</c>) using these same
/// errors.
/// </summary>
public static class RoleAssignmentErrors
{
    public const string PrimaryKey = "role_assignment_pkey";

    public const string UserForeignKey = "role_assignment_user_id_fkey";

    public const string RoleForeignKey = "role_assignment_role_id_fkey";

    /// <summary>
    /// <c>CHECK (valid_to IS NULL OR valid_to &gt;= created_at)</c>. Unnamed in the DDL, so this is
    /// PostgreSQL's generated name (the second table-level CHECK of <c>access.role_assignment</c>;
    /// pinned by a catalog test so a drift is caught). Only a backstop: the create handler
    /// rejects a past <c>validTo</c> before it ever reaches the database (<see cref="ValidToInPast"/>),
    /// and this mapping only covers the instants between that check and the audit stamp of
    /// <c>created_at</c> on save.
    /// </summary>
    public const string ValidToNotBeforeCreatedAtCheck = "role_assignment_check1";

    public static readonly Error UserNotFound =
        new("USER_NOT_FOUND", "userId does not reference an existing user.");

    public static readonly Error RoleNotFound =
        new("ROLE_NOT_FOUND", "roleId does not reference an existing role.");

    /// <summary>
    /// <c>validTo</c> is not in the future: the assignment would never grant anything, and the
    /// database forbids a <c>valid_to</c> earlier than the row's own <c>created_at</c>. Not a
    /// numbered rule (shape-adjacent, but time-dependent, so it is checked by the handler with
    /// <see cref="TimeProvider"/>, not by the pure validator): reported as a 422 with its own code.
    /// </summary>
    public static readonly Error ValidToInPast =
        new("ROLE_ASSIGNMENT_VALID_TO_IN_PAST", "'validTo' must be in the future.");

    /// <summary>ACC-R2: <c>scopeEntityId</c> is not a row of the table <c>scopeType</c> names.</summary>
    public static Error ScopeEntityNotFound(string scopeType) =>
        new("ACC-R2", $"scopeEntityId does not reference an existing {scopeType} entity.");

    /// <summary>
    /// The caller's own <c>ROLE_ASSIGNMENT:UPDATE</c> does not cover the scope of the assignment
    /// being revoked (ACC-R4/ACC-R10 applied to the row of a write endpoint).
    /// </summary>
    public static readonly Error RevokeScopeNotCovered =
        Error.Forbidden("Your ROLE_ASSIGNMENT:UPDATE permission does not cover the scope of this role assignment.");

    /// <summary>Two concurrent idempotent creates with the same client id.</summary>
    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A role assignment with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Role assignment '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The role assignment has changed since the ETag sent in If-Match was obtained.");
}
