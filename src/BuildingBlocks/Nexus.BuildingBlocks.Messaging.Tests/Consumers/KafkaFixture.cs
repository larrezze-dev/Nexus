using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Kafka;

namespace Nexus.BuildingBlocks.Messaging.Tests.Consumers;

/// <summary>
/// A real Kafka broker (same image as compose, <c>apache/kafka:4.3.2</c>, via Testcontainers.Kafka)
/// and a real Confluent Schema Registry (same image as compose) on a private network. The registry
/// reaches the broker through the extra listener <c>kafka:19092</c>. Same fixture as the relay's
/// tests (Nexus.OutboxRelay.Tests), trimmed to what the consumer tests use. Topic auto-creation is
/// disabled, as in compose (<c>KAFKA_AUTO_CREATE_TOPICS_ENABLE=false</c>).
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

    /// <summary>Creates <paramref name="topic"/> and its <c>.DLQ</c> (as tools/create-topics.sh does).</summary>
    public Task CreateTopicWithDlqAsync(string topic) => CreateTopicsAsync(topic, $"{topic}.DLQ");

    /// <summary>Creates the given topics (1 partition each); existing ones are left alone.</summary>
    public async Task CreateTopicsAsync(params string[] topics)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync(
                topics.Select(t => new TopicSpecification { Name = t, NumPartitions = 1, ReplicationFactor = 1 }));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
        {
        }
    }

    /// <summary>Reads up to <paramref name="expected"/> records of <paramref name="topic"/> from the beginning (raw bytes).</summary>
    public List<ConsumeResult<string?, byte[]?>> ReadAll(string topic, int expected, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string?, byte[]?>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = $"consumer-tests-reader-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));

        var results = new List<ConsumeResult<string?, byte[]?>>();
        var deadline = DateTime.UtcNow + timeout;
        while (results.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(250));
            if (result is { IsPartitionEOF: false, Message: not null })
            {
                results.Add(result);
            }
        }

        consumer.Close();
        return results;
    }

    /// <summary>Committed offset of <paramref name="groupId"/> on partition 0 of <paramref name="topic"/> (negative = none).</summary>
    public long CommittedOffset(string groupId, string topic)
    {
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = false,
        }).Build();
        var committed = consumer.Committed([new TopicPartition(topic, 0)], TimeSpan.FromSeconds(10));
        return committed.Single().Offset.Value;
    }
}
