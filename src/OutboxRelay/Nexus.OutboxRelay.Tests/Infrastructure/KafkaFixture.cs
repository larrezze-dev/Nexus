using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Kafka;

namespace Nexus.OutboxRelay.Tests.Infrastructure;

/// <summary>
/// A real Kafka broker (same image as compose, <c>apache/kafka:4.3.2</c>, via Testcontainers.Kafka)
/// and a real Confluent Schema Registry (same image as compose, generic container) on a private
/// network. The registry reaches the broker through the extra listener <c>kafka:19092</c>.
/// <c>auto.create.topics.enable=false</c>, as in compose.
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private const string KafkaImage = "apache/kafka:4.3.2";
    private const string SchemaRegistryImage = "confluentinc/cp-schema-registry:8.3.2";
    private const ushort SchemaRegistryPort = 8081;

    private INetwork _network = null!;
    private KafkaContainer _kafka = null!;
    private IContainer _schemaRegistry = null!;

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public string SchemaRegistryUrl => $"http://{_schemaRegistry.Hostname}:{_schemaRegistry.GetMappedPublicPort(SchemaRegistryPort)}";

    public async ValueTask InitializeAsync()
    {
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        _kafka = new KafkaBuilder(KafkaImage)
            .WithNetwork(_network)
            .WithListener("kafka:19092")
            // As compose and production (conventions-events.md §5): a produce or metadata request
            // never creates a topic, so the "missing topic" cases are real.
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
            .Build();
        await _kafka.StartAsync();

        _schemaRegistry = new ContainerBuilder(SchemaRegistryImage)
            .WithNetwork(_network)
            .WithNetworkAliases("schema-registry")
            .WithPortBinding(SchemaRegistryPort, assignRandomHostPort: true)
            .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
            .WithEnvironment("SCHEMA_REGISTRY_LISTENERS", $"http://0.0.0.0:{SchemaRegistryPort}")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS", "PLAINTEXT://kafka:19092")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("SCHEMA_REGISTRY_SCHEMA_COMPATIBILITY_LEVEL", "backward")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(SchemaRegistryPort).ForPath("/subjects")))
            .Build();
        await _schemaRegistry.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_schemaRegistry is not null)
        {
            await _schemaRegistry.DisposeAsync();
        }

        if (_kafka is not null)
        {
            await _kafka.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }
    }

    public CachedSchemaRegistryClient CreateRegistryClient() => new(new SchemaRegistryConfig { Url = SchemaRegistryUrl });

    /// <summary>
    /// Freezes the real Schema Registry process (<c>docker pause</c>): requests to it hang until
    /// they time out. Pause rather than stop keeps the mapped host port (a restart would get a new
    /// random one, unreachable for clients built with the old URL). Always pair with
    /// <see cref="UnpauseRegistryAsync"/>.
    /// </summary>
    public Task PauseRegistryAsync() => _schemaRegistry.PauseAsync();

    public Task UnpauseRegistryAsync() => _schemaRegistry.UnpauseAsync();

    /// <summary>Whether the broker reports the topic in its metadata.</summary>
    public bool TopicExists(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        return admin.GetMetadata(TimeSpan.FromSeconds(10)).Topics.Any(t => t.Topic == topic && t.Error.Code == ErrorCode.NoError);
    }

    /// <summary>Creates a topic (the broker, like the real ones, never auto-creates).</summary>
    public async Task CreateTopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    /// <summary>Registers <paramref name="schemaJson"/> under <c>&lt;topic&gt;-value</c>; returns (id, subject version).</summary>
    public async Task<(int Id, int Version)> RegisterAsync(string topic, string schemaJson)
    {
        using var client = CreateRegistryClient();
        var subject = $"{topic}-value";
        var id = await client.RegisterSchemaAsync(subject, new Schema(schemaJson, SchemaType.Avro));
        var registered = await client.LookupSchemaAsync(subject, new Schema(schemaJson, SchemaType.Avro), ignoreDeletedSchemas: true);
        return (id, registered.Version);
    }

    /// <summary>Reads every message currently in <paramref name="topic"/> from the beginning.</summary>
    public List<ConsumeResult<string, byte[]>> ReadAll(string topic, int expected, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = $"relay-tests-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var results = new List<ConsumeResult<string, byte[]>>();
        var deadline = DateTime.UtcNow + timeout;
        while (results.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is { IsPartitionEOF: false, Message: not null })
            {
                results.Add(result);
            }
        }

        consumer.Close();
        return results;
    }
}
