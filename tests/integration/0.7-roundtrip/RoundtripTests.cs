using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Avro;
using Avro.Generic;
using Avro.IO;
using ch.nussli.nexus.org;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Correlation;
using Npgsql;
using Shouldly;

namespace Nexus.Integration.Roundtrip07.Tests;

/// <summary>
/// Step 0.7 Gate D: <c>POST /v1/legal-entities</c> (step 0.4) → <c>outbox.message</c> →
/// relay → <c>nexus.org.legal-entity.created</c> (Avro) → a <see cref="NexusConsumer{TEvent}"/>,
/// all real (PostgreSQL, Kafka, Schema Registry, the org API and the relay hosted in-process).
/// </summary>
public sealed class RoundtripTests(RoundtripFixture fixture) : IClassFixture<RoundtripFixture>
{
    private static readonly Uri LegalEntities = new("/v1/legal-entities", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Log(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    [Fact(DisplayName = "Gate D 0.7 (conventions-events §2 envelope / §4 relay): creating a LegalEntity via the API puts its Avro event on nexus.org.legal-entity.created in < 2 s, keyed by the LE id, with the HTTP correlation id in nexus-correlation-id")]
    public async Task CreateLegalEntity_PublishesAvroEventToKafkaUnderTwoSeconds()
    {
        using var admin = fixture.CreateClientFor(fixture.Org.Admin.Oid);
        // A brand-new relay (new producer, new Schema Registry client, nothing cached) that already
        // holds leadership: what is measured is the first event it publishes, cold producer
        // connection / InitProducerId / schema lookup included, but not its leader election (the
        // 5 s leader retry is not the contract). Test order does not matter: this resets the relay.
        await fixture.RestartRelayAsync();
        using var topicReader = TopicReader.Subscribe(fixture.Kafka.BootstrapServers, RoundtripFixture.Topic);

        // Both events are held to the same bound, measured from just before the request is sent.
        var firstLatency = new Stopwatch();
        var firstCorrelationId = Guid.CreateVersion7();
        var first = await CreateLegalEntityAsync(admin, firstCorrelationId, firstLatency);
        topicReader.WaitFor(first.Id, TimeSpan.FromSeconds(10));
        firstLatency.Stop();
        Log($"First event of a freshly restarted relay, request sent -> readable: {firstLatency.Elapsed.TotalMilliseconds:0} ms.");
        firstLatency.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), "the first event of a freshly restarted relay must also be published within 2 s");

        var correlationId = Guid.CreateVersion7();
        var latency = new Stopwatch();
        var created = await CreateLegalEntityAsync(admin, correlationId, latency);
        var record = topicReader.WaitFor(created.Id, TimeSpan.FromSeconds(10));
        latency.Stop();
        Log($"Publish latency, request sent -> message readable on {RoundtripFixture.Topic}: {latency.Elapsed.TotalMilliseconds:0} ms.");
        latency.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), "the relay must publish within 2 s of the request");
        created.EchoedCorrelationId.ShouldBe(correlationId.ToString("D"), "the API echoes the correlation id it was given");

        // Key = aggregate id = the LE id.
        record.Message.Key.ShouldBe(created.Id.ToString("D"));

        // Envelope headers (conventions-events.md §2): the HTTP correlation id travels end to end.
        Header(record, EventHeaderNames.CorrelationId).ShouldBe(correlationId.ToString("D"));
        Header(record, EventHeaderNames.EventType).ShouldBe("org.legal-entity.created");
        Header(record, EventHeaderNames.AggregateType).ShouldBe("legal-entity");
        Header(record, EventHeaderNames.AggregateId).ShouldBe(created.Id.ToString("D"));
        Header(record, EventHeaderNames.AggregateVersion).ShouldBe("1");
        Header(record, EventHeaderNames.SchemaVersion).ShouldBe("1");
        Guid.TryParse(Header(record, EventHeaderNames.EventId), out _).ShouldBeTrue();
        Header(record, EventHeaderNames.ActorUserId).ShouldBe(fixture.Org.Admin.Id.ToString("D"));

        // The outbox row it came from is PUBLISHED (the relay marks rows at the end of its batch,
        // so it may land just after the record is readable).
        await WaitUntilAsync(
            async () => await fixture.Database.ScalarAsync<string>(
                "SELECT status FROM outbox.message WHERE aggregate_id = $1 AND event_type = 'org.legal-entity.created'", created.Id) == "PUBLISHED",
            "the outbox row is marked PUBLISHED");

        // Valid Avro: Confluent wire format (magic byte + the id the registry gave the contract's
        // .avsc), and the bytes decode with the contract's schema to the created aggregate.
        var value = record.Message.Value;
        value[0].ShouldBe((byte)0);
        ReadSchemaId(value).ShouldBe(fixture.SchemaId);
        using var registry = fixture.Kafka.CreateRegistryClient();
        (await registry.LookupSchemaAsync(
            $"{RoundtripFixture.Topic}-value",
            new Confluent.SchemaRegistry.Schema(fixture.SchemaJson, Confluent.SchemaRegistry.SchemaType.Avro),
            ignoreDeletedSchemas: false)).Id.ShouldBe(fixture.SchemaId);

        var schema = (RecordSchema)Schema.Parse(fixture.SchemaJson);
        using var stream = new MemoryStream(value, 5, value.Length - 5);
        var datum = new GenericDatumReader<GenericRecord>(schema, schema).Read(null!, new BinaryDecoder(stream));
        stream.Position.ShouldBe(stream.Length, "no trailing bytes after the Avro record");
        datum["id"].ShouldBe(created.Id);
        datum["code"].ShouldBe(created.Code);
        datum["currencyCode"].ShouldBe("CHF");
        datum["countryCode"].ShouldBe("CH");
        datum["isActive"].ShouldBe(true);
        datum["groupId"].ShouldBe(fixture.Org.GroupId);
    }

    [Fact(DisplayName = "conventions-events §7 (idempotent consumer): a NexusConsumer that is stopped between the effect's commit and the offset commit and restarted applies each event exactly once")]
    public async Task Consumer_RestartedMidStream_AppliesEachEventOnce()
    {
        using var admin = fixture.CreateClientFor(fixture.Org.Admin.Oid);
        await using var host = fixture.BuildConsumerHost();
        using var dlq = new KafkaDlqPublisher(fixture.Kafka.BootstrapServers, TimeProvider.System);
        var group = $"operations.roundtrip-{Guid.CreateVersion7():N}";
        var useCase = group["operations.".Length..];

        // Three legal entities created through the API; the relay publishes them.
        var first = await CreateLegalEntityAsync(admin, Guid.NewGuid());
        var second = await CreateLegalEntityAsync(admin, Guid.NewGuid());
        var third = await CreateLegalEntityAsync(admin, Guid.NewGuid());
        var created = new[] { first, second, third };
        await WaitUntilAsync(async () => await PublishedCountAsync(created) == 3, "the relay publishes the three events");

        // First run: "crashes" right after the first LE's effect is committed, before its offset is.
        using var firstRun = new RoundtripConsumer(Runtime(host, dlq), useCase) { HangAfter = first.Id };
        await firstRun.StartAsync(Ct);
        try
        {
            await firstRun.Hanging.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        }
        finally
        {
            await firstRun.StopAsync(CancellationToken.None);
        }

        (await EffectCountAsync(group, first.Id)).ShouldBe(1, "the effect was committed before the crash");

        // Second run, same group: the offset was never committed, so the first LE is redelivered.
        using var secondRun = new RoundtripConsumer(Runtime(host, dlq), useCase);
        await secondRun.StartAsync(Ct);
        try
        {
            await WaitUntilAsync(
                async () => await EffectCountAsync(group, second.Id) == 1 && await EffectCountAsync(group, third.Id) == 1,
                "the restarted consumer processes the remaining events");
        }
        finally
        {
            await secondRun.StopAsync(CancellationToken.None);
        }

        foreach (var le in created)
        {
            (await EffectCountAsync(group, le.Id)).ShouldBe(1, $"legal entity {le.Code}: effect applied exactly once");
            (await fixture.Database.ScalarAsync<long>(
                "SELECT count(*) FROM inbox.processed_message WHERE consumer = $1 AND event_id = (SELECT id FROM outbox.message WHERE aggregate_id = $2 AND event_type = 'org.legal-entity.created')",
                group, le.Id)).ShouldBe(1, $"legal entity {le.Code}: exactly one inbox row");
        }

        // The redelivered record was skipped by the inbox without running the handler again.
        firstRun.Received.ShouldNotContain(second.Id.ToString("D"), "the first run stopped at the first LE");
        firstRun.Received.ShouldNotContain(third.Id.ToString("D"), "the first run stopped at the first LE");
        secondRun.Received.ShouldContain(first.Id.ToString("D"), "its offset was never committed, so the record is redelivered");
        secondRun.HandledLegalEntities.ShouldNotContain(first.Id, "the inbox skipped the redelivered record");
    }

    private static string Header(ConsumeResult<string, byte[]> record, string name) =>
        Encoding.UTF8.GetString(record.Message.Headers.GetLastBytes(name));

    private static int ReadSchemaId(byte[] value) =>
        (value[1] << 24) | (value[2] << 16) | (value[3] << 8) | value[4];

    private NexusConsumerRuntime Runtime(ServiceProvider host, IDlqPublisher dlq) =>
        new(
            host.GetRequiredService<IServiceScopeFactory>(),
            dlq,
            new RetryOptions(),
            TimeProvider.System,
            NullLoggerFactory.Instance,
            Microsoft.Extensions.Options.Options.Create(new NexusKafkaOptions
            {
                BootstrapServers = fixture.Kafka.BootstrapServers,
                SchemaRegistryUrl = fixture.Kafka.SchemaRegistryUrl,
            }),
            new ConsumerHealthRegistry(TimeProvider.System));

    private async Task<CreatedLegalEntity> CreateLegalEntityAsync(HttpClient client, Guid correlationId, Stopwatch? sendTimer = null)
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var id = Guid.CreateVersion7();
        var code = "LE-RT-" + tag;
        using var request = new HttpRequestMessage(HttpMethod.Post, LegalEntities)
        {
            Content = JsonContent.Create(new
            {
                Id = id,
                LegalName = "Roundtrip Legal Entity " + tag,
                DisplayName = (string?)null,
                GroupId = fixture.Org.GroupId,
                Code = code,
                CurrencyCode = "CHF",
                CountryCode = "CH",
            }),
        };
        request.Headers.Add(CorrelationMiddleware.HeaderName, correlationId.ToString("D"));
        sendTimer?.Restart();
        var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;
        body.GetProperty("id").GetGuid().ShouldBe(id);
        response.Headers.TryGetValues(CorrelationMiddleware.HeaderName, out var echoed).ShouldBeTrue();
        return new CreatedLegalEntity(id, code, echoed!.Single());
    }

    private async Task<long> PublishedCountAsync(IEnumerable<CreatedLegalEntity> entities) =>
        await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM outbox.message WHERE status = 'PUBLISHED' AND event_type = 'org.legal-entity.created' AND aggregate_id = ANY($1)",
            entities.Select(e => e.Id).ToArray());

    private Task<long> EffectCountAsync(string group, Guid legalEntityId) =>
        fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM {RoundtripFixture.EffectSchema}.effect_row WHERE consumer = $1 AND entity_id = $2", group, legalEntityId);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for: " + what);
            }

            await Task.Delay(50, Ct);
        }
    }

    private sealed record CreatedLegalEntity(Guid Id, string Code, string EchoedCorrelationId);

    /// <summary>
    /// A <see cref="NexusConsumer{TEvent}"/> of the real <c>nexus.org.legal-entity.created</c> topic
    /// whose effect inserts one row in the same transaction as its inbox row.
    /// </summary>
    private sealed class RoundtripConsumer : NexusConsumer<LegalEntityCreated>
    {
        private readonly List<Guid> _handled = [];
        private readonly ConcurrentQueue<string?> _received = new();

        public RoundtripConsumer(NexusConsumerRuntime runtime, string useCase)
            : base(runtime.CreateOptions("operations", useCase, RoundtripFixture.Topic), runtime)
        {
        }

        /// <summary>When set, the consumer blocks after fully processing (effect + inbox committed) this legal entity's record, before its offset is committed.</summary>
        public Guid? HangAfter { get; init; }

        /// <summary>Keys of every record handed to the pipeline (delivered by Kafka), before it is processed.</summary>
        public IReadOnlyCollection<string?> Received => _received;

        public TaskCompletionSource Hanging { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<Guid> HandledLegalEntities
        {
            get
            {
                lock (_handled)
                {
                    return [.. _handled];
                }
            }
        }

        protected override async Task HandleAsync(LegalEntityCreated message, EventEnvelope envelope, IServiceProvider services, CancellationToken ct)
        {
            lock (_handled)
            {
                _handled.Add(message.id);
            }

            var uow = services.GetRequiredService<IUnitOfWork>();
            await using var command = new NpgsqlCommand(
                $"INSERT INTO {RoundtripFixture.EffectSchema}.effect_row (id, consumer, event_id, entity_id) VALUES ($1, $2, $3, $4)",
                uow.Connection,
                uow.Transaction);
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.CreateVersion7() });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = Options.GroupId });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = envelope.EventId });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = message.id });
            await command.ExecuteNonQueryAsync(ct);
        }

        protected override async Task ProcessAsync(ConsumeResult<string?, byte[]?> result, CancellationToken ct)
        {
            _received.Enqueue(result.Message.Key);
            await base.ProcessAsync(result, ct);
            if (HangAfter is { } target && result.Message.Key == target.ToString("D"))
            {
                // Simulates a crash between "effect committed" and "offset committed".
                Hanging.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        }
    }

    /// <summary>A plain consumer on the topic (own group, from the beginning), subscribed and assigned before the test acts.</summary>
    private sealed class TopicReader : IDisposable
    {
        private readonly IConsumer<string, byte[]> _consumer;
        private readonly Dictionary<string, ConsumeResult<string, byte[]>> _seen = [];

        private TopicReader(IConsumer<string, byte[]> consumer) => _consumer = consumer;

        public static TopicReader Subscribe(string bootstrapServers, string topic)
        {
            var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"roundtrip-reader-{Guid.NewGuid():N}",
                // Earliest, not Latest: with Latest the start position is resolved lazily after assignment and a
                // message published right after it can be skipped. Messages are matched by key anyway.
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            }).Build();
            consumer.Subscribe(topic);

            // Poll until the partition is assigned, so nothing published afterwards can be missed.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (consumer.Assignment.Count == 0)
            {
                if (DateTime.UtcNow > deadline)
                {
                    consumer.Dispose();
                    throw new TimeoutException("The reader consumer was never assigned a partition.");
                }

                consumer.Consume(TimeSpan.FromMilliseconds(100));
            }

            return new TopicReader(consumer);
        }

        public ConsumeResult<string, byte[]> WaitFor(Guid key, TimeSpan timeout)
        {
            var wanted = key.ToString("D");
            var deadline = DateTime.UtcNow + timeout;
            while (!_seen.ContainsKey(wanted))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"No message with key {wanted} within {timeout.TotalSeconds:0.#} s.");
                }

                if (_consumer.Consume(TimeSpan.FromMilliseconds(20)) is { Message: not null } result)
                {
                    _seen[result.Message.Key] = result;
                }
            }

            return _seen[wanted];
        }

        public void Dispose()
        {
            _consumer.Close();
            _consumer.Dispose();
        }
    }
}
