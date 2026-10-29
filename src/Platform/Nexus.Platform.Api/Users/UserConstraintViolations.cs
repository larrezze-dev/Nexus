using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Api.Users;

/// <summary>
/// Database constraints of <c>org.app_user</c>/<c>org.party</c> whose violation is an expected
/// outcome of a user command, translated to the same errors the application layer uses
/// (<see cref="UserErrors"/>). Everything else stays a 500.
/// </summary>
internal static class UserConstraintViolations
{
    public static IServiceCollection AddUserConstraintViolations(this IServiceCollection services) =>
        services
            // Rule ORG-R9: email unique among active users (partial, case-insensitive index).
            .AddNexusConstraintViolation(UserErrors.EmailActiveUniqueConstraint, UserErrors.EmailAlreadyUsed)
            .AddNexusConstraintViolation(UserErrors.ExternalSubjectUniqueConstraint, UserErrors.OidAlreadyUsed)
            .AddNexusConstraintViolation(UserErrors.BusinessUnitForeignKey, UserErrors.BusinessUnitNotFound)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(UserErrors.UserPrimaryKey, UserErrors.IdAlreadyUsed);
}
