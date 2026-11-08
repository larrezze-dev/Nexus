using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Kafka;

namespace Nexus.Integration.Roundtrip07.Tests;

/// <summary>
/// A real Kafka broker (same image as compose, <c>apache/kafka:4.3.2</c>) and a real Confluent
/// Schema Registry (same image as compose) on a private network. Same fixture as the relay's and
/// the consumer's tests, trimmed to what the round trip needs. Topic auto-creation is disabled,
/// as in compose (<c>KAFKA_AUTO_CREATE_TOPICS_ENABLE=false</c>).
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

    /// <summary>Creates <paramref name="topic"/> and its <c>.DLQ</c> (what <c>tools/create-topics.sh</c> does locally).</summary>
    public async Task CreateTopicWithDlqAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 },
            new TopicSpecification { Name = topic + ".DLQ", NumPartitions = 1, ReplicationFactor = 1 },
        ]);
    }

    /// <summary>Registers <paramref name="schemaJson"/> under <c>&lt;topic&gt;-value</c> (what <c>tools/register-schemas.sh</c> does locally); returns its id.</summary>
    public async Task<int> RegisterAsync(string topic, string schemaJson)
    {
        using var client = CreateRegistryClient();
        return await client.RegisterSchemaAsync($"{topic}-value", new Schema(schemaJson, SchemaType.Avro));
    }
}
