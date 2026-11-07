using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ch.nussli.nexus.org;
using Confluent.Kafka;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.BuildingBlocks.Testing;

namespace Nexus.BuildingBlocks.Messaging.Tests.Consumers;

[CollectionDefinition(Name)]
public sealed class KafkaPostgresFixtures : ICollectionFixture<KafkaFixture>, ICollectionFixture<PostgresFixture>
{
    public const string Name = "kafka+postgres";
}

/// <summary>
/// A consumer of the real <c>nexus.org.legal-entity.created</c> event (generated class
/// <see cref="LegalEntityCreated"/>), on a per-test topic, whose effect is scripted by the test.
/// </summary>
internal sealed class TestLegalEntityConsumer : NexusConsumer<LegalEntityCreated>
{
    private readonly Func<LegalEntityCreated, EventEnvelope, IServiceProvider, CancellationToken, Task> _effect;
    private int _processed;

    public TestLegalEntityConsumer(
        ConsumerOptions options,
        NexusConsumerRuntime runtime,
        Func<LegalEntityCreated, EventEnvelope, IServiceProvider, CancellationToken, Task> effect)
        : base(options, runtime)
    {
        _effect = effect;
    }

    public string GroupId => Options.GroupId;

    /// <summary>Handler invocations per event id (every attempt counts).</summary>
    public ConcurrentDictionary<Guid, int> Invocations { get; } = new();

    /// <summary>When set, after the N-th processed record (before its commit) the consumer blocks until stopped.</summary>
    public int? HangAfterProcessed { get; init; }

    public TaskCompletionSource Hanging { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Task HandleAsync(LegalEntityCreated message, EventEnvelope envelope, IServiceProvider services, CancellationToken ct)
    {
        Invocations.AddOrUpdate(envelope.EventId, 1, (_, n) => n + 1);
        return _effect(message, envelope, services, ct);
    }

    protected internal override async Task ProcessAsync(ConsumeResult<string?, byte[]?> result, CancellationToken ct)
    {
        await base.ProcessAsync(result, ct);
        if (HangAfterProcessed is { } n && Interlocked.Increment(ref _processed) == n)
        {
            // Simulates a crash between "effect committed" and "offset committed".
            Hanging.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}

internal static class ConsumerTestKit
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    public static string NewTopic() => $"nexus.test.legal-entity-{Guid.CreateVersion7():N}.created";

    /// <summary>Short back-offs (same 1 attempt + 3 retries as production) so the DLQ tests stay fast.</summary>
    public static RetryOptions FastRetry => new()
    {
        MaxAttempts = new RetryOptions().MaxAttempts,
        Delays = [TimeSpan.FromMilliseconds(20)],
        DlqPublishInitialBackoff = TimeSpan.FromMilliseconds(200),
        DlqPublishMaxBackoff = TimeSpan.FromSeconds(1),
    };

    /// <summary>
    /// The runtime of a test consumer. <paramref name="dlq"/> is owned by the caller (dispose it:
    /// <see cref="KafkaDlqPublisher"/> holds a librdkafka producer).
    /// </summary>
    public static NexusConsumerRuntime Runtime(
        IServiceProvider root, KafkaFixture kafka, IDlqPublisher dlq, ConsumerHealthRegistry? health = null) =>
        new(
            root.GetRequiredService<IServiceScopeFactory>(),
            dlq,
            FastRetry,
            TimeProvider.System,
            NullLoggerFactory.Instance,
            Microsoft.Extensions.Options.Options.Create(new NexusKafkaOptions
            {
                BootstrapServers = kafka.BootstrapServers,
                SchemaRegistryUrl = kafka.SchemaRegistryUrl,
            }),
            health ?? new ConsumerHealthRegistry(TimeProvider.System));

    public static TestLegalEntityConsumer Consumer(
        NexusConsumerRuntime runtime,
        string topic,
        string useCase,
        Func<LegalEntityCreated, EventEnvelope, IServiceProvider, CancellationToken, Task> effect,
        int? hangAfterProcessed = null) =>
        new(runtime.CreateOptions("operations", useCase, topic, ActingIdentity.OnBehalfOfActor), runtime, effect)
        {
            HangAfterProcessed = hangAfterProcessed,
        };

    public static LegalEntityCreated LegalEntity(string code = "LE-TEST") => new()
    {
        id = Guid.CreateVersion7(),
        partyId = Guid.CreateVersion7(),
        groupId = Guid.CreateVersion7(),
        code = code,
        currencyCode = "CHF",
        countryCode = "CH",
        isActive = true,
    };

    public static EventEnvelope Envelope(Guid aggregateId, Guid? eventId = null) => new(
        EventId: eventId ?? Guid.CreateVersion7(),
        EventType: "org.legal-entity.created",
        AggregateType: "legal-entity",
        AggregateId: aggregateId,
        AggregateVersion: 1,
        OccurredAt: DateTimeOffset.UtcNow,
        ActorUserId: Guid.CreateVersion7(),
        SchemaVersion: 1,
        CorrelationId: Guid.CreateVersion7(),
        CausationId: Guid.CreateVersion7(),
        LegalEntityId: aggregateId);

    /// <summary>The A7 envelope headers, as the relay writes them (conventions-events.md §2).</summary>
    public static Headers Headers(EventEnvelope e)
    {
        static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
        var headers = new Headers
        {
            { EventHeaderNames.EventId, B(e.EventId.ToString("D")) },
            { EventHeaderNames.EventType, B(e.EventType) },
            { EventHeaderNames.AggregateType, B(e.AggregateType) },
            { EventHeaderNames.AggregateId, B(e.AggregateId.ToString("D")) },
            { EventHeaderNames.AggregateVersion, B(e.AggregateVersion.ToString(CultureInfo.InvariantCulture)) },
            { EventHeaderNames.OccurredAt, B(e.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture)) },
            { EventHeaderNames.SchemaVersion, B(e.SchemaVersion.ToString(CultureInfo.InvariantCulture)) },
            { EventHeaderNames.CorrelationId, B(e.CorrelationId.ToString("D")) },
            { EventHeaderNames.CausationId, B(e.CausationId.ToString("D")) },
        };
        if (e.ActorUserId is { } actor)
        {
            headers.Add(EventHeaderNames.ActorUserId, B(actor.ToString("D")));
        }

        if (e.LegalEntityId is { } le)
        {
            headers.Add(EventHeaderNames.LegalEntityId, B(le.ToString("D")));
        }

        return headers;
    }

    /// <summary>Produces a real Avro record (Confluent wire format, schema auto-registered) with the envelope headers.</summary>
    public static async Task<byte[]> ProduceAsync(KafkaFixture kafka, string topic, LegalEntityCreated value, EventEnvelope envelope)
    {
        var bytes = await SerializeAsync(kafka, topic, value);
        await ProduceRawAsync(kafka, topic, value.id.ToString("D"), bytes, Headers(envelope));
        return bytes;
    }

    public static async Task<byte[]> SerializeAsync(KafkaFixture kafka, string topic, LegalEntityCreated value)
    {
        using var registry = kafka.CreateRegistryClient();
        return await new AvroSerializer<LegalEntityCreated>(registry)
            .SerializeAsync(value, new SerializationContext(MessageComponentType.Value, topic));
    }

    public static async Task ProduceRawAsync(KafkaFixture kafka, string topic, string? key, byte[]? value, Headers headers)
    {
        using var producer = new ProducerBuilder<string?, byte[]?>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();
        await producer.ProduceAsync(topic, new Message<string?, byte[]?> { Key = key, Value = value, Headers = headers });
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for: {what}");
    }

    public static string Header(Headers headers, string name) =>
        headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : throw new KeyNotFoundException(name);
}
