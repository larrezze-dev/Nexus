using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Web.Authentication;
using Nexus.BuildingBlocks.Web.Correlation;

namespace Nexus.BuildingBlocks.Web;

/// <summary>
/// Single entry point of the web building blocks for a NEXUS HTTP service
/// (<c>builder.Services.AddNexusWeb(builder.Configuration)</c>). Each concern also has its own
/// extension, so a host can wire them one by one if it must.
/// </summary>
public static class NexusWebServiceCollectionExtensions
{
    /// <summary>
    /// Registers correlation (<see cref="NexusCorrelationExtensions.AddNexusCorrelation"/>) and
    /// JWT authentication with the HTTP <c>ICurrentUser</c>
    /// (<see cref="NexusAuthenticationExtensions.AddNexusAuthentication"/>). Requires
    /// <c>AddNexusApplication</c> (for <c>IIdGenerator</c> and <c>TimeProvider</c>).
    /// Pipeline: <c>app.UseNexusCorrelation()</c> first, then <c>UseAuthentication()</c>.
    /// </summary>
    public static IServiceCollection AddNexusWeb(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddNexusCorrelation();
        services.AddNexusAuthentication(configuration);

        return services;
    }
}
