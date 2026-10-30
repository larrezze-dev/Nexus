using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.BusinessUnits;

namespace Nexus.Platform.Api.BusinessUnits;

/// <summary>
/// Database constraints of <c>org.business_unit</c> whose violation is an expected outcome of a
/// business unit command, translated to the same errors the application layer uses
/// (<see cref="BusinessUnitErrors"/>). Everything else stays a 500.
/// </summary>
internal static class BusinessUnitConstraintViolations
{
    public static IServiceCollection AddBusinessUnitConstraintViolations(this IServiceCollection services) =>
        services
            .AddNexusConstraintViolation(BusinessUnitErrors.CodeUniqueConstraint, BusinessUnitErrors.CodeAlreadyUsed)
            .AddNexusConstraintViolation(BusinessUnitErrors.LegalEntityForeignKey, BusinessUnitErrors.LegalEntityNotFound)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(BusinessUnitErrors.BusinessUnitPrimaryKey, BusinessUnitErrors.IdAlreadyUsed);
}
