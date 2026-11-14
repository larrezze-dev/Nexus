using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Currencies;

namespace Nexus.Platform.Api.Currencies;

/// <summary>
/// Database constraints of <c>org.currency</c> whose violation is an expected outcome of a
/// currency command, translated to the same errors the application layer uses
/// (<see cref="CurrencyErrors"/>). Everything else stays a 500.
/// </summary>
internal static class CurrencyConstraintViolations
{
    public static IServiceCollection AddCurrencyConstraintViolations(this IServiceCollection services) =>
        // Two concurrent creates of the same code: the loser sees a conflict.
        services.AddNexusConstraintViolation(CurrencyErrors.CurrencyPrimaryKey, CurrencyErrors.CodeAlreadyUsed);
}
