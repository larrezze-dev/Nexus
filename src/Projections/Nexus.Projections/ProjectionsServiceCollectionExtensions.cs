using Confluent.SchemaRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.Projections.Migrations;
using Nexus.Projections.Runtime;

namespace Nexus.Projections;

/// <summary>Wiring of the <c>nexus-projections</c> runtime.</summary>
public static class ProjectionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the projection runtime: options (<c>Nexus:Kafka</c>, <c>Nexus:ClickHouse</c>,
    /// <c>Nexus:Projections</c>, <c>Nexus:Consumers:Health</c>), the ClickHouse migrator and sink, the DLQ
    /// publisher, retry settings, metrics, the consumer health registry and lag probe, the Schema Registry
    /// client, the rebuild command, and two hosted services in this order: the migration applier, then
    /// the <see cref="ProjectionWorker"/> (one consumer per projection).
    /// <para>
    /// It does NOT register any projection: projections are <see cref="Abstractions.IProjection"/>
    /// singletons added separately (Program.cs calls <c>AddNexusProjectionDefinitions()</c>; tests add their
    /// own). <see cref="ProjectionCatalog"/> collects every registered <c>IProjection</c>.
    /// </para>
    /// <para>Expose health with <c>app.MapHealthChecks("/health")</c> (the check is registered here).</para>
    /// </summary>
    public static IServiceCollection AddNexusProjectionsRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Every time-dependent piece takes TimeProvider (CLAUDE.md §3).
        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();

        services.Configure<NexusKafkaOptions>(configuration.GetSection(NexusKafkaOptions.SectionName));
        services.Configure<ClickHouseOptions>(configuration.GetSection(ClickHouseOptions.SectionName));
        services.Configure<ProjectionOptions>(configuration.GetSection(ProjectionOptions.SectionName));

        services.TryAddSingleton(new RetryOptions());
        services.TryAddSingleton<ConsumerHealthRegistry>();
        services.TryAddSingleton<IDlqPublisher>(sp =>
        {
            var kafka = sp.GetRequiredService<IOptions<NexusKafkaOptions>>().Value;
            return new KafkaDlqPublisher(kafka.BootstrapServers, sp.GetRequiredService<TimeProvider>(), kafka.DlqMessageTimeout);
        });
        services.TryAddSingleton<ISchemaRegistryClient>(sp =>
        {
            var kafka = sp.GetRequiredService<IOptions<NexusKafkaOptions>>().Value;
            var options = sp.GetRequiredService<IOptions<ProjectionOptions>>().Value;
            return new CachedSchemaRegistryClient(new SchemaRegistryConfig
            {
                Url = kafka.SchemaRegistryUrl,
                RequestTimeoutMs = (int)options.SchemaRegistryRequestTimeout.TotalMilliseconds,
            });
        });

        services.TryAddSingleton<ClickHouseConnectionProvider>();
        services.TryAddSingleton<IProjectionSink, ClickHouseProjectionSink>();
        services.TryAddSingleton<ClickHouseMigrator>();
        services.TryAddSingleton<ProjectionMetrics>();
        services.TryAddSingleton<ProjectionCatalog>();
        services.TryAddSingleton<ProjectionRuntime>();
        services.TryAddSingleton<ProjectionRebuilder>();

        // A projection subscribes to a regex or a topic list: our probe expands it (registered first, so
        // AddNexusConsumerHealth keeps it instead of its single-topic default).
        services.TryAddSingleton<IConsumerLagProbe, ProjectionLagProbe>();
        services.AddHealthChecks().AddNexusConsumerHealth();

        // Order matters: hosted services start sequentially, migrations before any consumer.
        services.AddHostedService<ClickHouseMigrationHostedService>();
        services.AddHostedService<ProjectionWorker>();

        services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        return services;
    }
}
