using System.Text;
using Avro;
using Avro.Generic;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Postgres;
using Nexus.OutboxRelay.Publishing;
using Npgsql;
using NpgsqlTypes;
using Shouldly;

namespace Nexus.OutboxRelay.Tests.Infrastructure;

/// <summary>Counts what one relay instance actually got acknowledged by Kafka.</summary>
internal sealed class CountingPublisher(IEventPublisher inner) : IEventPublisher
{
    private readonly object _gate = new();
    private readonly List<(Guid Id, DateTime At)> _published = [];

    /// <summary>Runs inside <see cref="PublishAsync"/> after each acknowledged publish, with the running count.</summary>
    public Func<int, Task>? AfterPublished { get; set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _published.Count;
            }
        }
    }

    /// <summary>UTC time of the first acknowledged publish at or after <paramref name="since"/>.</summary>
    public DateTime? FirstPublishSince(DateTime since)
    {
        lock (_gate)
        {
            return _published.Where(p => p.At >= since).Select(p => (DateTime?)p.At).FirstOrDefault();
        }
    }

    public async Task<PublishResult> PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var result = await inner.PublishAsync(message, cancellationToken);
        if (result.Succeeded)
        {
            int count;
            lock (_gate)
            {
                _published.Add((message.Id, DateTime.UtcNow));
                count = _published.Count;
            }

            if (AfterPublished is { } hook)
            {
                await hook(count);
            }
        }

        return result;
    }
}

/// <summary>
/// The leadership of one relay "process" that the test can crash: after <see cref="CrashAsync"/>
/// the process is dead (it never acquires again) and its leader session is terminated by the
/// server (<c>pg_terminate_backend</c>): no unlock, no commit, no graceful shutdown.
/// </summary>
internal sealed class CrashableLeadership(IRelayLeadership inner) : IRelayLeadership
{
    private readonly object _gate = new();
    private bool _crashed;
    private int? _leaderPid;

    public int? LeaderPid
    {
        get
        {
            lock (_gate)
            {
                return _leaderPid;
            }
        }
    }

    public async Task<IRelayLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_crashed)
            {
                return null;
            }
        }

        var lease = await inner.TryAcquireAsync(cancellationToken);
        if (lease is PostgresRelayLease pg)
        {
            lock (_gate)
            {
                _leaderPid = pg.BackendProcessId;
            }
        }

        return lease;
    }

    public async Task CrashAsync(PostgresFixture db)
    {
        int pid;
        lock (_gate)
        {
            _crashed = true;
            pid = _leaderPid ?? throw new InvalidOperationException("This instance never became leader.");
        }

        (await db.ScalarAsync<bool>("SELECT pg_terminate_backend($1)", pid)).ShouldBeTrue("the leader session is killed");
    }
}

/// <summary>Rows of the probe event (Avro <see cref="SchemaJson"/>), inserted in bulk.</summary>
internal static class ProbeRows
{
    public const string SchemaJson =
        """
        {"type":"record","name":"RelayProbeCreated","namespace":"ch.nussli.nexus.test","fields":[
          {"name":"probeId","type":{"type":"string","logicalType":"uuid"}},
          {"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}}]}
        """;

    public static readonly RecordSchema Schema = (RecordSchema)Avro.Schema.Parse(SchemaJson);

    public sealed record Row(Guid Id, Guid AggregateId, long Version, DateTimeOffset CreatedAt, string Payload);

    /// <summary>
    /// <paramref name="count"/> events over <paramref name="aggregates"/> aggregates picked at random
    /// (fixed seed): each event carries the next version of its aggregate and a strictly increasing
    /// <c>created_at</c>, the way one writer per aggregate produces them. <c>amount</c> = version.
    /// </summary>
    public static List<Row> Interleaved(int count, IReadOnlyList<Guid> aggregates, int seed, DateTimeOffset start)
    {
        var random = new Random(seed);
        var versions = aggregates.ToDictionary(a => a, _ => 0L);
        var rows = new List<Row>(count);
        for (var i = 0; i < count; i++)
        {
            var aggregate = aggregates[random.Next(aggregates.Count)];
            var version = ++versions[aggregate];
            rows.Add(new Row(Guid.CreateVersion7(), aggregate, version, start.AddMilliseconds(i), Payload(aggregate, version)));
        }

        return rows;
    }

    public static string Payload(Guid aggregate, long version)
    {
        var record = new GenericRecord(Schema);
        record.Add("probeId", aggregate);
        record.Add("amount", new AvroDecimal(version + 0.0000m));
        return AvroJson.Encode(Schema, record);
    }

    /// <summary>One INSERT for all <paramref name="rows"/> (arrange as the schema owner; fires the NOTIFY trigger).</summary>
    public static async Task InsertAsync(PostgresFixture db, string topic, IReadOnlyList<Row> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var command = db.Admin.CreateCommand(
            """
            INSERT INTO outbox.message (id, topic, aggregate_type, aggregate_id, aggregate_version, event_type, schema_version, headers, payload, created_at, status, attempts)
            SELECT u.id, $1, 'relay-probe', u.agg, u.ver, 'test.relay-probe.created', 1, u.h::jsonb, u.p, u.c, 'PENDING', 0
            FROM unnest($2::uuid[], $3::uuid[], $4::bigint[], $5::text[], $6::text[], $7::timestamptz[]) AS u(id, agg, ver, h, p, c)
            """);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = topic });
        command.Parameters.Add(new NpgsqlParameter<Guid[]> { TypedValue = rows.Select(r => r.Id).ToArray() });
        command.Parameters.Add(new NpgsqlParameter<Guid[]> { TypedValue = rows.Select(r => r.AggregateId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter<long[]> { TypedValue = rows.Select(r => r.Version).ToArray() });
        command.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = rows.Select(_ => OutboxRows.Headers()).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = rows.Select(r => r.Payload).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset[]> { TypedValue = rows.Select(r => r.CreatedAt).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz });
        (await command.ExecuteNonQueryAsync()).ShouldBe(rows.Count);
    }

    public static async Task<long> CountAsync(PostgresFixture db, string topic, string status) =>
        await db.ScalarAsync<long>("SELECT count(*) FROM outbox.message WHERE topic = $1 AND status = $2", topic, status);

    public static string NewTopic() => $"nexus.test.relay-probe-{Guid.NewGuid():N}.created";
}

/// <summary>
/// Scratch directories laid out like <c>contracts/events</c> (ADR-0018: the relay encodes each row
/// with <c>&lt;root&gt;/&lt;context&gt;/&lt;aggregate&gt;.&lt;event&gt;.v&lt;n&gt;.avsc</c>).
/// </summary>
internal static class TestContracts
{
    private static readonly Lazy<string> ProbeRoot = new(() => Create(("test/relay-probe.created.v1.avsc", ProbeRows.SchemaJson)));

    /// <summary>A root holding <c>test/relay-probe.created.v1.avsc</c> = <see cref="ProbeRows.SchemaJson"/>.</summary>
    public static string Probe => ProbeRoot.Value;

    /// <summary>A fresh root with <paramref name="files"/> (path relative to the root, content).</summary>
    public static string Create(params (string RelativePath, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-relay-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return root;
    }
}

/// <summary>A message as read back from Kafka (single-partition topic, offset order).</summary>
internal sealed record ConsumedEvent(long Offset, string Key, Guid EventId, long AggregateVersion, byte[] Value)
{
    public static ConsumedEvent From(ConsumeResult<string, byte[]> result)
    {
        string Header(string name) => Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes(name));
        return new ConsumedEvent(
            result.Offset.Value,
            result.Message.Key,
            Guid.Parse(Header(RelayKafkaHeaders.EventId)),
            long.Parse(Header(RelayKafkaHeaders.AggregateVersion), System.Globalization.CultureInfo.InvariantCulture),
            result.Message.Value);
    }
}

internal static class KafkaRead
{
    /// <summary>High watermark of partition 0 = number of records in a fresh single-partition topic
    /// written by an idempotent, non-transactional producer (offsets are contiguous).</summary>
    public static long HighWatermark(KafkaFixture kafka, string topic)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"relay-tests-wm-{Guid.NewGuid():N}",
        }).Build();
        return consumer.QueryWatermarkOffsets(new TopicPartition(topic, 0), TimeSpan.FromSeconds(10)).High.Value;
    }

    /// <summary>Every record of the topic, in offset order; asserts nothing was left unread.</summary>
    public static List<ConsumedEvent> ReadToEnd(KafkaFixture kafka, string topic)
    {
        var high = HighWatermark(kafka, topic);
        var read = kafka.ReadAll(topic, checked((int)high), TimeSpan.FromSeconds(60)).Select(ConsumedEvent.From).OrderBy(e => e.Offset).ToList();
        read.Count.ShouldBe((int)high, "every record up to the high watermark is read");
        return read;
    }

    /// <summary>
    /// EV-R3 under at-least-once delivery: per key, version v never appears before v-1 has
    /// appeared (a re-delivery of earlier versions is allowed, a gap is not), and every version
    /// 1..n of the aggregate appears.
    /// </summary>
    public static void ShouldHaveNoGapsPerKey(IEnumerable<ConsumedEvent> events, IReadOnlyDictionary<string, long> expectedMaxVersionByKey)
    {
        foreach (var group in events.GroupBy(e => e.Key))
        {
            long maxSeen = 0;
            foreach (var e in group)
            {
                e.AggregateVersion.ShouldBeLessThanOrEqualTo(maxSeen + 1, $"key {group.Key}: version {e.AggregateVersion} at offset {e.Offset} published before version {maxSeen + 1}");
                maxSeen = Math.Max(maxSeen, e.AggregateVersion);
            }

            maxSeen.ShouldBe(expectedMaxVersionByKey[group.Key], $"key {group.Key}: every version is published");
        }

        events.Select(e => e.Key).Distinct().Count().ShouldBe(expectedMaxVersionByKey.Count);
    }
}

internal static class Poll
{
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0.#} s: {because}");
            }

            await Task.Delay(50);
        }
    }

    public static Task UntilAsync(Func<bool> condition, TimeSpan timeout, string because) =>
        UntilAsync(() => Task.FromResult(condition()), timeout, because);
}

/// <summary>A real relay instance: real leadership (svc_relay), real Kafka producer, real registry encoder.</summary>
internal sealed class RelayInstance : IAsyncDisposable
{
    private readonly TestMeterFactory _meters = new();
    private readonly KafkaEventPublisher _kafkaPublisher;
    private bool _started;

    public RelayInstance(PostgresFixture db, KafkaFixture kafka, RelayOptions options, Confluent.SchemaRegistry.ISchemaRegistryClient? registry = null, string? contractsDirectory = null)
    {
        options.KafkaBootstrapServers = kafka.BootstrapServers;
        options.SchemaRegistryUrl = kafka.SchemaRegistryUrl;
        options.ContractsDirectory = contractsDirectory ?? TestContracts.Probe;
        Registry = registry ?? kafka.CreateRegistryClient();
        _kafkaPublisher = new KafkaEventPublisher(new SchemaRegistryPayloadEncoder(Registry, options.ContractsDirectory), Options.Create(options), NullLogger<KafkaEventPublisher>.Instance);
        Publisher = new CountingPublisher(_kafkaPublisher);
        Leadership = new CrashableLeadership(new PostgresRelayLeadership(db.GetConnectionString("svc_relay"), NullLoggerFactory.Instance));
        Worker = new RelayWorker(Leadership, Publisher, new RelayMetrics(_meters), Options.Create(options), TimeProvider.System, NullLogger<RelayWorker>.Instance);
    }

    public Confluent.SchemaRegistry.ISchemaRegistryClient Registry { get; }

    public CountingPublisher Publisher { get; }

    public CrashableLeadership Leadership { get; }

    public RelayWorker Worker { get; }

    public async Task StartAsync()
    {
        await Worker.StartAsync(CancellationToken.None);
        _started = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await Worker.StopAsync(CancellationToken.None);
        }

        Worker.Dispose();
        _kafkaPublisher.Dispose();
        Registry.Dispose();
        _meters.Dispose();
    }
}
