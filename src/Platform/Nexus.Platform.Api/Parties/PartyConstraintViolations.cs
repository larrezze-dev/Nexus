using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Parties;

namespace Nexus.Platform.Api.Parties;

/// <summary>
/// Database constraints of <c>org.party</c> whose violation is an expected outcome of a party
/// command, translated to the same errors the application layer uses (<see cref="PartyErrors"/>).
/// Everything else stays a 500.
/// </summary>
internal static class PartyConstraintViolations
{
    public static IServiceCollection AddPartyConstraintViolations(this IServiceCollection services) =>
        services
            // Two concurrent idempotent creates with the same client id: the loser sees a conflict.
            .AddNexusConstraintViolation(PartyErrors.PartyPrimaryKey, PartyErrors.IdAlreadyUsed);
}
