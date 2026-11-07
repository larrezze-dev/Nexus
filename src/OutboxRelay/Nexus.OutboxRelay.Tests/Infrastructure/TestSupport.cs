using System.Diagnostics.Metrics;
using System.Text;
using Avro;
using Avro.Generic;
using Avro.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Postgres;
using Npgsql;
using NpgsqlTypes;
using Shouldly;

namespace Nexus.OutboxRelay.Tests.Infrastructure;

/// <summary>Arrange/assert helpers over the real <c>outbox.message</c> table.</summary>
internal static class OutboxRows
{
    public const string DefaultTopic = "nexus.test.relay-probe.created";

    public static async Task ClearAsync(PostgresFixture db)
    {
        await using var command = db.Admin.CreateCommand("DELETE FROM outbox.message");
        await command.ExecuteNonQueryAsync();
    }

    public static string Headers(Guid? legalEntityId = null) =>
        $$"""
        {"occurred_at":"2026-11-07T10:15:30.123456Z","actor_user_id":"{{Guid.CreateVersion7()}}","correlation_id":"{{Guid.CreateVersion7()}}","causation_id":"{{Guid.CreateVersion7()}}","legal_entity_id":{{(legalEntityId is { } le ? $"\"{le}\"" : "null")}}}
        """;

    /// <summary>Inserts a row as the schema owner (arrange only; business roles insert via UnitOfWork).</summary>
    public static async Task<Guid> InsertAsync(
        PostgresFixture db,
        Guid aggregateId,
        long aggregateVersion,
        DateTimeOffset createdAt,
        string payload = "{}",
        string topic = DefaultTopic,
        int schemaVersion = 1,
        string status = "PENDING",
        int attempts = 0,
        string? headers = null,
        Guid? id = null)
    {
        var rowId = id ?? Guid.CreateVersion7();
        await using var command = db.Admin.CreateCommand(
            "INSERT INTO outbox.message (id, topic, aggregate_type, aggregate_id, aggregate_version, event_type, schema_version, headers, payload, created_at, status, attempts) " +
            "VALUES ($1, $2, 'relay-probe', $3, $4, 'test.relay-probe.created', $5, $6::jsonb, $7, $8, $9, $10)");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = rowId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = topic });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = aggregateId });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = aggregateVersion });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = schemaVersion });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = headers ?? Headers() });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = payload, NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = createdAt });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = status });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = attempts });
        await command.ExecuteNonQueryAsync();
        return rowId;
    }

    public static async Task<(string Status, int Attempts, string? LastError, DateTimeOffset? PublishedAt)> GetAsync(PostgresFixture db, Guid id)
    {
        await using var command = db.Admin.CreateCommand("SELECT status, attempts, last_error, published_at FROM outbox.message WHERE id = $1");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Row {id} not found.");
        }

        return (
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    /// <summary>The relay as it runs in production: role <c>svc_relay</c>.</summary>
    public static PostgresRelayLeadership Leadership(PostgresFixture db) =>
        new(db.GetConnectionString("svc_relay"), NullLoggerFactory.Instance);

    public static async Task<IRelayLease> AcquireAsync(PostgresFixture db)
    {
        var lease = await Leadership(db).TryAcquireAsync(CancellationToken.None);
        return lease ?? throw new InvalidOperationException("Leader lock unexpectedly held by someone else.");
    }
}

/// <summary>Avro JSON encoding of a datum, the way <c>OutboxWriter</c> stores payloads (EV-R1).</summary>
internal static class AvroJson
{
    public static string Encode(Schema schema, object datum)
    {
        using var stream = new MemoryStream();
        var encoder = new JsonEncoder(schema, stream);
        new GenericDatumWriter<object>(schema).Write(datum, encoder);
        encoder.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Decodes Confluent wire format (magic + id + binary) with <paramref name="schema"/>.</summary>
    public static (int SchemaId, object Datum) DecodeWire(byte[] value, Schema schema)
    {
        value[0].ShouldBe((byte)0);
        var id = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(1, 4));
        using var stream = new MemoryStream(value, 5, value.Length - 5);
        var datum = new GenericDatumReader<object>(schema, schema).Read(null!, new BinaryDecoder(stream));
        stream.Position.ShouldBe(stream.Length, "the whole Avro body is consumed");
        return (id, datum);
    }
}

internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
        {
            meter.Dispose();
        }
    }
}

/// <summary>Collects <c>nexus_outbox_failed_total</c> measurements of one <see cref="TestMeterFactory"/>.</summary>
internal sealed class FailedCounterListener : IDisposable
{
    private readonly MeterListener _listener = new();

    public FailedCounterListener(IMeterFactory factory)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == factory && instrument.Name == RelayMetrics.FailedTotalName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _total, value));
        _listener.Start();
    }

    private long _total;

    public long Total => Interlocked.Read(ref _total);

    public void Dispose() => _listener.Dispose();
}

/// <summary>A publisher whose outcome per row is scripted by the test.</summary>
internal sealed class ScriptedPublisher(Func<OutboxMessage, PublishResult> script) : IEventPublisher
{
    public List<OutboxMessage> Attempted { get; } = [];

    /// <summary>Optional <see cref="IEventPublisher.TryDefer"/> script.</summary>
    public Func<OutboxMessage, PublishResult?>? Defer { get; init; }

    public PublishResult? TryDefer(OutboxMessage message) => Defer?.Invoke(message);

    public Task<PublishResult> PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Attempted.Add(message);
        return Task.FromResult(script(message));
    }
}

/// <summary>An in-memory batch (no database) for pure processor tests.</summary>
internal sealed class FakeBatch(params OutboxMessage[] messages) : IOutboxBatch
{
    public IReadOnlyList<OutboxMessage> Messages { get; } = messages;

    public Dictionary<Guid, DateTimeOffset> Published { get; } = [];

    public Dictionary<Guid, (int Attempts, string Error, bool Terminal)> Failed { get; } = [];

    public bool Committed { get; private set; }

    /// <summary>Confirmations left before the fake reports the lock as lost (null: never).</summary>
    public int? LeadershipChecksBeforeLoss { get; set; }

    public int Confirmations { get; private set; }

    public Task ConfirmLeadershipAsync(CancellationToken cancellationToken)
    {
        Confirmations++;
        if (LeadershipChecksBeforeLoss is { } left)
        {
            if (left == 0)
            {
                throw new RelayLeadershipLostException("lost (test)");
            }

            LeadershipChecksBeforeLoss = left - 1;
        }

        return Task.CompletedTask;
    }

    public void MarkPublished(Guid id, DateTimeOffset publishedAt) => Published[id] = publishedAt;

    public void MarkFailed(Guid id, int attempts, string lastError, bool terminal) => Failed[id] = (attempts, lastError, terminal);

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static OutboxMessage Message(Guid aggregateId, long version, int attempts = 0) =>
        new(Guid.CreateVersion7(), OutboxRows.DefaultTopic, "relay-probe", aggregateId, version, "test.relay-probe.created", 1,
            OutboxRows.Headers(), "{}", DateTimeOffset.UtcNow, attempts);
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Collects the formatted messages of one logger.</summary>
internal sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_messages)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}

/// <summary>
/// A minimal Schema Registry stand-in: answers every request with <see cref="Status"/> and a
/// Confluent-style error body, so the encoder's HTTP status classification can be tested.
/// </summary>
internal sealed class FakeRegistryServer : IDisposable
{
    private readonly System.Net.HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private readonly int _errorCode;
    private readonly string? _rawBody;

    /// <param name="status">HTTP status of every answer.</param>
    /// <param name="errorCode">Confluent <c>error_code</c> of the JSON body (default: status × 100 + 1).</param>
    /// <param name="rawBody">When set, sent as <c>text/html</c> instead of the JSON error body (a proxy's error page).</param>
    public FakeRegistryServer(int status, int? errorCode = null, string? rawBody = null)
    {
        Status = status;
        _errorCode = errorCode ?? (status * 100) + 1;
        _rawBody = rawBody;
        int port;
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _loop = Task.Run(ServeAsync);
    }

    public int Status { get; }

    public string Url { get; }

    private int _requests;

    public int Requests => Volatile.Read(ref _requests);

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _listener.Close();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            System.Net.HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (System.Net.HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            Interlocked.Increment(ref _requests);
            var body = Encoding.UTF8.GetBytes(_rawBody ?? $$"""{"error_code":{{_errorCode}},"message":"fake registry answer {{Status}}"}""");
            context.Response.StatusCode = Status;
            context.Response.ContentType = _rawBody is null ? "application/vnd.schemaregistry.v1+json" : "text/html";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }
}
