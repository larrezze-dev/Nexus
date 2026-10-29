using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.LegalEntities;

namespace Nexus.Platform.Api.LegalEntities;

/// <summary>
/// Database constraints of <c>org.legal_entity</c> whose violation is an expected outcome of a
/// legal entity command, translated to the same errors the application layer uses
/// (<see cref="LegalEntityErrors"/>). Everything else stays a 500.
/// </summary>
internal static class LegalEntityConstraintViolations
{
    public static IServiceCollection AddLegalEntityConstraintViolations(this IServiceCollection services) =>
        services
            .AddNexusConstraintViolation(LegalEntityErrors.CodeUniqueConstraint, LegalEntityErrors.CodeAlreadyUsed)
            .AddNexusConstraintViolation(LegalEntityErrors.GroupForeignKey, LegalEntityErrors.GroupNotFound)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(LegalEntityErrors.LegalEntityPrimaryKey, LegalEntityErrors.IdAlreadyUsed);
}
