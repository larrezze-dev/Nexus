using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Groups;

namespace Nexus.Platform.Api.Groups;

/// <summary>
/// Database constraints of <c>org."group"</c> whose violation is an expected outcome of a group
/// command, translated to the same errors the application layer uses (<see cref="GroupErrors"/>).
/// Everything else stays a 500.
/// </summary>
internal static class GroupConstraintViolations
{
    public static IServiceCollection AddGroupConstraintViolations(this IServiceCollection services) =>
        services
            .AddNexusConstraintViolation(GroupErrors.CodeUniqueConstraint, GroupErrors.CodeAlreadyUsed)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(GroupErrors.GroupPrimaryKey, GroupErrors.IdAlreadyUsed);
}
