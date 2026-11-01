using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Roles;

namespace Nexus.Platform.Api.Roles;

/// <summary>
/// Database constraints of <c>access.role</c> whose violation is an expected outcome of a role
/// command, translated to the same errors the application layer uses (<see cref="RoleErrors"/>).
/// Everything else stays a 500.
/// </summary>
internal static class RoleConstraintViolations
{
    public static IServiceCollection AddRoleConstraintViolations(this IServiceCollection services) =>
        services
            .AddNexusConstraintViolation(RoleErrors.CodeUniqueConstraint, RoleErrors.CodeAlreadyUsed)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(RoleErrors.RolePrimaryKey, RoleErrors.IdAlreadyUsed);
}
