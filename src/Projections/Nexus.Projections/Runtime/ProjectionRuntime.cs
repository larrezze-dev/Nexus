using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Runtime;

/// <summary>
/// What every projection consumer (worker loop or rebuild) shares, bundled as one singleton: options,
/// the ClickHouse sink, the DLQ publisher, the retry policy settings, the clock, metrics, the health
/// registry and the Schema Registry client.
/// </summary>
public sealed class ProjectionRuntime
{
    public ProjectionRuntime(
        IOptions<ProjectionOptions> options,
        IOptions<NexusKafkaOptions> kafka,
        IProjectionSink sink,
        IDlqPublisher dlqPublisher,
        RetryOptions retryOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ProjectionMetrics metrics,
        ConsumerHealthRegistry health,
        ISchemaRegistryClient schemaRegistry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(kafka);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(dlqPublisher);
        ArgumentNullException.ThrowIfNull(retryOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(schemaRegistry);
        Options = options.Value;
        Options.Validate();
        Kafka = kafka.Value;
        Sink = sink;
        DlqPublisher = dlqPublisher;
        RetryOptions = retryOptions;
        TimeProvider = timeProvider;
        LoggerFactory = loggerFactory;
        Metrics = metrics;
        Health = health;
        SchemaRegistry = schemaRegistry;
    }

    public ProjectionOptions Options { get; }

    public NexusKafkaOptions Kafka { get; }

    public IProjectionSink Sink { get; }

    public IDlqPublisher DlqPublisher { get; }

    public RetryOptions RetryOptions { get; }

    public TimeProvider TimeProvider { get; }

    public ILoggerFactory LoggerFactory { get; }

    public ProjectionMetrics Metrics { get; }

    public ConsumerHealthRegistry Health { get; }

    public ISchemaRegistryClient SchemaRegistry { get; }

    /// <summary>
    /// librdkafka settings of a projection consumer: group <c>projections.&lt;name&gt;</c>, no auto commit,
    /// no auto offset store (offsets are committed explicitly after the ClickHouse insert), earliest reset,
    /// explicit <c>max.poll.interval.ms</c>, and a metadata refresh short enough for a regex subscription
    /// to pick up new topics.
    /// </summary>
    public ConsumerConfig CreateConsumerConfig(IProjection projection, bool enablePartitionEof = false)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new ConsumerConfig
        {
            BootstrapServers = Kafka.BootstrapServers,
            GroupId = ProjectionCatalog.GroupId(projection),
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            MaxPollIntervalMs = (int)Kafka.MaxPollInterval.TotalMilliseconds,
            TopicMetadataRefreshIntervalMs = (int)Options.TopicMetadataRefreshInterval.TotalMilliseconds,
            EnablePartitionEof = enablePartitionEof,
        };
    }

    internal static TimeSpan NextBackoff(TimeSpan current, TimeSpan max)
    {
        var doubled = current * 2;
        return doubled > max ? max : doubled;
    }
}
