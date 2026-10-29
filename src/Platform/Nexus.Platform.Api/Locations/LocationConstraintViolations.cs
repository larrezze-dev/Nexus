using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Locations;

namespace Nexus.Platform.Api.Locations;

/// <summary>
/// Database constraints of <c>org.location</c> whose violation is an expected outcome of a
/// location command, translated to the same errors the application layer uses
/// (<see cref="LocationErrors"/>). Everything else stays a 500.
/// </summary>
internal static class LocationConstraintViolations
{
    public static IServiceCollection AddLocationConstraintViolations(this IServiceCollection services) =>
        services
            .AddNexusConstraintViolation(LocationErrors.CodeUniqueConstraint, LocationErrors.CodeAlreadyUsed)
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(LocationErrors.LocationPrimaryKey, LocationErrors.IdAlreadyUsed);
}
