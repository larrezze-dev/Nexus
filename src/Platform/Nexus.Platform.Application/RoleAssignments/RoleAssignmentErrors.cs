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

    public static readonly Error UserNotFound =
        new("USER_NOT_FOUND", "userId does not reference an existing user.");

    public static readonly Error RoleNotFound =
        new("ROLE_NOT_FOUND", "roleId does not reference an existing role.");

    /// <summary>Two concurrent idempotent creates with the same client id.</summary>
    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A role assignment with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Role assignment '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The role assignment has changed since the ETag sent in If-Match was obtained.");
}
