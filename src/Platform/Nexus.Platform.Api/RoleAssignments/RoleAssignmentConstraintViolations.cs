using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.RoleAssignments;

namespace Nexus.Platform.Api.RoleAssignments;

/// <summary>
/// Database constraints of <c>access.role_assignment</c> whose violation is an expected outcome
/// of a role-assignment command, translated to the same errors the application layer uses
/// (<see cref="RoleAssignmentErrors"/>). Everything else stays a 500. Kept in its own extension
/// method (not added to an existing <c>AddXConstraintViolation</c> call site) only to keep this
/// feature's registration self-contained; see <c>RoleAssignmentEndpoints</c>'s sibling note on
/// why <c>Program.cs</c> itself is not edited here.
/// </summary>
internal static class RoleAssignmentConstraintViolations
{
    public static IServiceCollection AddRoleAssignmentConstraintViolations(this IServiceCollection services) =>
        services
            .AddNexusConstraintViolation(RoleAssignmentErrors.UserForeignKey, RoleAssignmentErrors.UserNotFound)
            .AddNexusConstraintViolation(RoleAssignmentErrors.RoleForeignKey, RoleAssignmentErrors.RoleNotFound)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(RoleAssignmentErrors.PrimaryKey, RoleAssignmentErrors.IdAlreadyUsed);
}
