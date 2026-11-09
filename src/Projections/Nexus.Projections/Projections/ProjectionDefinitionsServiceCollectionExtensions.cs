using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Projections;

public static class ProjectionDefinitionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the step 0.8 projections (<c>event_log</c>, <c>dim_legal_entity</c>, <c>dim_warehouse</c>)
    /// as <see cref="IProjection"/> singletons, plus the <see cref="TopicRegistry"/> they need.
    /// </summary>
    /// <param name="topicsYamlPath">Overrides the topic registry location (default: next to the binaries).
    /// The registry is loaded when the first projection is resolved, and throws on a malformed file.</param>
    public static IServiceCollection AddNexusProjectionDefinitions(this IServiceCollection services, string? topicsYamlPath = null)
    {
        services.AddSingleton(_ => TopicRegistry.LoadFile(topicsYamlPath));
        services.AddSingleton<IProjection>(sp => new EventLogProjection(
            sp.GetRequiredService<TopicRegistry>(), sp.GetRequiredService<ILogger<EventLogProjection>>()));
        services.AddSingleton<IProjection, DimLegalEntityProjection>();
        services.AddSingleton<IProjection, DimWarehouseProjection>();
        return services;
    }
}
