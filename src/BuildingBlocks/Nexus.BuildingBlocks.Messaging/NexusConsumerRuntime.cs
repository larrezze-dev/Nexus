using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Messaging.Retry;

namespace Nexus.BuildingBlocks.Messaging;

/// <summary>
/// The shared infrastructure every <see cref="NexusConsumer{TEvent}"/> of a host needs, bundled so a
/// concrete consumer has a single constructor dependency (singleton, registered by
/// <see cref="ConsumerHostServiceCollectionExtensions.AddNexusConsumers"/>):
/// <code>
/// public sealed class ReserveOnJobConfirmed(NexusConsumerRuntime runtime)
///     : NexusConsumer&lt;JobConfirmed&gt;(runtime.CreateOptions("operations", "reserve-on-job-confirmed",
///         "nexus.project.job.confirmed", ActingIdentity.OnBehalfOfActor), runtime)
/// </code>
/// </summary>
public sealed class NexusConsumerRuntime
{
    public NexusConsumerRuntime(
        IServiceScopeFactory scopeFactory,
        IDlqPublisher dlqPublisher,
        RetryOptions retryOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        IOptions<NexusKafkaOptions> kafka,
        ConsumerHealthRegistry health)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(dlqPublisher);
        ArgumentNullException.ThrowIfNull(retryOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(kafka);
        ArgumentNullException.ThrowIfNull(health);
        ScopeFactory = scopeFactory;
        DlqPublisher = dlqPublisher;
        RetryOptions = retryOptions;
        TimeProvider = timeProvider;
        LoggerFactory = loggerFactory;
        Kafka = kafka.Value;
        Health = health;
    }

    public IServiceScopeFactory ScopeFactory { get; }

    public IDlqPublisher DlqPublisher { get; }

    public RetryOptions RetryOptions { get; }

    public TimeProvider TimeProvider { get; }

    public ILoggerFactory LoggerFactory { get; }

    public NexusKafkaOptions Kafka { get; }

    public ConsumerHealthRegistry Health { get; }

    /// <summary>Options for a consumer of <paramref name="topic"/> with group <c>&lt;service&gt;.&lt;useCase&gt;</c>.</summary>
    public ConsumerOptions CreateOptions(
        string service, string useCase, string topic, ActingIdentity identity = ActingIdentity.System) =>
        new()
        {
            Service = service,
            UseCase = useCase,
            Topic = topic,
            BootstrapServers = Kafka.BootstrapServers,
            SchemaRegistryUrl = Kafka.SchemaRegistryUrl,
            Identity = identity,
            MaxPollInterval = Kafka.MaxPollInterval,
        };
}
