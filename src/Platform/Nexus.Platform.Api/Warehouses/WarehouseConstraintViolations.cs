using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Warehouses;

namespace Nexus.Platform.Api.Warehouses;

/// <summary>
/// Database constraints of <c>org.warehouse</c> whose violation is an expected outcome of a
/// warehouse command, translated to the same errors the application layer uses
/// (<see cref="WarehouseErrors"/>). Everything else stays a 500.
/// </summary>
internal static class WarehouseConstraintViolations
{
    public static IServiceCollection AddWarehouseConstraintViolations(this IServiceCollection services) =>
        services
            .AddNexusConstraintViolation(WarehouseErrors.CodeUniqueConstraint, WarehouseErrors.CodeAlreadyUsed)
            .AddNexusConstraintViolation(WarehouseErrors.LocationForeignKey, WarehouseErrors.LocationNotFound)
            .AddNexusConstraintViolation(WarehouseErrors.LegalEntityForeignKey, WarehouseErrors.LegalEntityNotFound)
            .AddNexusConstraintViolation(WarehouseErrors.BusinessUnitForeignKey, WarehouseErrors.BusinessUnitNotFound)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(WarehouseErrors.WarehousePrimaryKey, WarehouseErrors.IdAlreadyUsed);
}
