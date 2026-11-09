using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.BuildingBlocks.Analytics;

namespace Nexus.BuildingBlocks.Web.Audit;

/// <summary>DI wiring of the generic audit endpoint (<see cref="AuditEndpointExtensions.MapAuditEndpoint"/>).</summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything <c>MapAuditEndpoint()</c> needs: the analytics gateway
    /// (<see cref="AnalyticsServiceCollectionExtensions.AddNexusAnalytics"/>, requires
    /// <c>AddNexusPersistence</c>) and the validator of the endpoint's input. Lazy: the host starts
    /// and serves its OpenAPI document with no ClickHouse running.
    /// </summary>
    public static IServiceCollection AddNexusAudit(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddNexusAnalytics(configuration);
        services.TryAddSingleton<IValidator<AuditTrailQuery>, AuditTrailQueryValidator>();
        services.TryAddSingleton<AuditEndpointMarker>();

        return services;
    }
}

/// <summary>Marker that <see cref="AuditServiceCollectionExtensions.AddNexusAudit"/> ran (checked when mapping).</summary>
internal sealed class AuditEndpointMarker;
