using System.Text;
using Avro.Generic;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Nexus.BuildingBlocks.Messaging;
using Testcontainers.Kafka;

namespace Nexus.Projections.Tests.Runtime.Infrastructure;

/// <summary>
/// A real Kafka broker (same image as compose, <c>apache/kafka:4.3.2</c>) and a real Confluent Schema
/// Registry on a private network; copied from the relay's fixture. <c>auto.create.topics.enable=false</c>,
/// as in compose: every test creates its topics (and the <c>.DLQ</c> ones it expects to be written).
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private const string KafkaImage = "apache/kafka:4.3.2";
    private const string SchemaRegistryImage = "confluentinc/cp-schema-registry:8.3.2";
    private const ushort SchemaRegistryPort = 8081;

    private INetwork _network = null!;
    private KafkaContainer _kafka = null!;
    private IContainer _schemaRegistry = null!;
    private IProducer<string?, byte[]?> _producer = null!;
    private CachedSchemaRegistryClient _registry = null!;

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
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
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

        _producer = new ProducerBuilder<string?, byte[]?>(new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();
        _registry = new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = SchemaRegistryUrl });
    }

    public async ValueTask DisposeAsync()
    {
        _producer?.Dispose();
        _registry?.Dispose();
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

    /// <summary>Freezes the Schema Registry (<c>docker pause</c>): requests hang until they time out. Pair with <see cref="UnpauseRegistryAsync"/>.</summary>
    public Task PauseRegistryAsync() => _schemaRegistry.PauseAsync();

    public Task UnpauseRegistryAsync() => _schemaRegistry.UnpauseAsync();

    public IAdminClient CreateAdmin() =>
        new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();

    /// <summary>Creates topics (the broker never auto-creates).</summary>
    public async Task CreateTopicsAsync(params string[] topics)
    {
        using var admin = CreateAdmin();
        try
        {
            await admin.CreateTopicsAsync(topics.Select(t => new TopicSpecification { Name = t, NumPartitions = 1, ReplicationFactor = 1 }));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
        {
        }
    }

    /// <summary>Serializes <paramref name="payload"/> with the Confluent wire format (auto-registering its schema under <c>&lt;topic&gt;-value</c>).</summary>
    public async Task<byte[]> SerializeAsync(string topic, GenericRecord payload)
    {
        var serializer = new AvroSerializer<GenericRecord>(_registry);
        return await serializer.SerializeAsync(payload, new SerializationContext(MessageComponentType.Value, topic));
    }

    /// <summary>Produces raw bytes with raw headers; returns the offset.</summary>
    public async Task<long> ProduceRawAsync(string topic, string? key, byte[]? value, Headers headers)
    {
        var result = await _producer.ProduceAsync(topic, new Message<string?, byte[]?> { Key = key, Value = value, Headers = headers });
        return result.Offset.Value;
    }

    /// <summary>The envelope headers of conventions-events.md §2 for <paramref name="envelope"/>.</summary>
    public static Headers HeadersFor(EventEnvelope envelope, params string[] omit)
    {
        var headers = new Headers();
        void Add(string name, string? value)
        {
            if (!omit.Contains(name))
            {
                headers.Add(name, Encoding.UTF8.GetBytes(value ?? string.Empty));
            }
        }

        Add(EventHeaderNames.EventId, envelope.EventId.ToString());
        Add(EventHeaderNames.EventType, envelope.EventType);
        Add(EventHeaderNames.AggregateType, envelope.AggregateType);
        Add(EventHeaderNames.AggregateId, envelope.AggregateId.ToString());
        Add(EventHeaderNames.AggregateVersion, envelope.AggregateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(EventHeaderNames.OccurredAt, envelope.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        Add(EventHeaderNames.ActorUserId, envelope.ActorUserId?.ToString());
        Add(EventHeaderNames.SchemaVersion, envelope.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(EventHeaderNames.CorrelationId, envelope.CorrelationId.ToString());
        Add(EventHeaderNames.CausationId, envelope.CausationId.ToString());
        Add(EventHeaderNames.LegalEntityId, envelope.LegalEntityId?.ToString());
        return headers;
    }

    /// <summary>Committed offset of <paramref name="groupId"/> on partition 0 of <paramref name="topic"/>, or null when none.</summary>
    public async Task<long?> CommittedAsync(string groupId, string topic)
    {
        using var admin = CreateAdmin();
        var result = await admin.ListConsumerGroupOffsetsAsync(
            [new ConsumerGroupTopicPartitions(groupId, [new TopicPartition(topic, 0)])]);
        var offset = result.SelectMany(r => r.Partitions).Single().Offset;
        return offset.Value >= 0 ? offset.Value : null;
    }

    /// <summary>High watermark of partition 0 of <paramref name="topic"/>.</summary>
    public long HighWatermark(string topic)
    {
        using var consumer = new ConsumerBuilder<string?, byte[]?>(new ConsumerConfig { BootstrapServers = BootstrapServers, GroupId = "projections-tests-watermarks" }).Build();
        return consumer.QueryWatermarkOffsets(new TopicPartition(topic, 0), TimeSpan.FromSeconds(10)).High.Value;
    }

    /// <summary>Reads every message currently in <paramref name="topic"/> (raw), waiting for <paramref name="expected"/> at most <paramref name="timeout"/>.</summary>
    public List<ConsumeResult<string?, byte[]?>> ReadAll(string topic, int expected, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string?, byte[]?>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = $"projections-tests-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));
        var results = new List<ConsumeResult<string?, byte[]?>>();
        var deadline = DateTime.UtcNow + timeout;
        while (results.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(200));
            if (result is { IsPartitionEOF: false, Message: not null })
            {
                results.Add(result);
            }
        }

        consumer.Close();
        return results;
    }
}
