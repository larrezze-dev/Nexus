using Confluent.SchemaRegistry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.OutboxRelay.Postgres;
using Nexus.OutboxRelay.Publishing;

namespace Nexus.OutboxRelay;

/// <summary>Registers the relay's components (the host binds <see cref="RelayOptions"/> itself).</summary>
public static class RelayServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxRelay(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RelayMetrics>();
        services.AddSingleton<ISchemaRegistryClient>(sp =>
            new CachedSchemaRegistryClient(SchemaRegistryPayloadEncoder.CreateClientConfig(sp.GetRequiredService<IOptions<RelayOptions>>().Value)));
        services.AddSingleton<IPayloadEncoder, SchemaRegistryPayloadEncoder>();
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<IRelayLeadership, PostgresRelayLeadership>();
        services.AddHostedService<RelayWorker>();
        return services;
    }
}
