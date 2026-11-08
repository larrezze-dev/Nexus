extern alias relay;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using relay::Nexus.OutboxRelay;
using Npgsql;
using Shouldly;

namespace Nexus.Integration.Roundtrip07.Tests;

/// <summary>
/// The whole step 0.7 chain on real infrastructure (Testcontainers only):
/// PostgreSQL 17 with every Flyway migration + the <see cref="OrgFixture"/> data, Kafka + Schema
/// Registry with <c>nexus.org.legal-entity.created</c> created and its <c>.avsc</c> registered
/// (as <c>tools/create-topics.sh</c> / <c>tools/register-schemas.sh</c> do locally), the org API
/// (<c>Nexus.Platform.Api</c>) hosted in memory as <c>svc_platform</c>, and the real relay
/// (<c>AddOutboxRelay()</c>, as <c>svc_relay</c>, default options) hosted in the same process.
/// The consumer side is built per test (<see cref="BuildConsumerHost"/>) as <c>svc_operations</c>.
/// </summary>
public sealed class RoundtripFixture : WebAppFixture<Program>, IAsyncLifetime
{
    public const string Topic = "nexus.org.legal-entity.created";

    public const string ConsumerRole = "svc_operations";

    public const string EffectSchema = "test_roundtrip";

    private IHost _relay = null!;

    public OrgFixture Org { get; } = new();

    public KafkaFixture Kafka { get; } = new();

    public PostgresFixture Database => Org.Database;

    /// <summary>The Schema Registry id of the registered contract schema.</summary>
    public int SchemaId { get; private set; }

    /// <summary>The <c>.avsc</c> text exactly as it is in <c>contracts/events/org/</c>.</summary>
    public string SchemaJson { get; } = File.ReadAllText(RepoRoot.Find("contracts/events/org/legal-entity.created.v1.avsc"));

    public async ValueTask InitializeAsync()
    {
        // Postgres and Kafka are independent: start them together.
        await Task.WhenAll(Org.InitializeAsync().AsTask(), Kafka.InitializeAsync().AsTask());

        // Every topic of contracts/events/topics.yaml with its .avsc registered, as `make up` +
        // tools/register-schemas.sh leave a developer's environment: creating a LegalEntity also
        // emits org.party.created, and an unknown topic would only be held back by the relay.
        foreach (var (topic, schemaPath) in ReadTopics())
        {
            await Kafka.CreateTopicWithDlqAsync(topic);
            var id = await Kafka.RegisterAsync(topic, await File.ReadAllTextAsync(schemaPath));
            if (topic == Topic)
            {
                SchemaId = id;
            }
        }

        SchemaId.ShouldBeGreaterThan(0);

        await CreateEffectTableAsync();

        _relay = BuildRelay();
        await _relay.StartAsync();
    }

    /// <summary>A client whose requests carry a valid JWT with <paramref name="oid"/>.</summary>
    public HttpClient CreateClientFor(string oid)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuer.IssueToken(oid: oid));
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_relay is not null)
        {
            await _relay.StopAsync();
            _relay.Dispose();
        }

        await base.DisposeAsync();
        await Kafka.DisposeAsync();
        await Org.DisposeAsync();
    }

    /// <summary>
    /// Stops and disposes the running relay (its leader session ends, so the advisory lock is
    /// released), then starts a brand-new one from <see cref="BuildRelay"/>: new producer, new
    /// Schema Registry client, nothing cached. Returns only once the old leader is gone and the new
    /// instance holds leadership, so neither the 5 s leader retry nor two leaders can leak into
    /// a measurement. The producer is deliberately NOT warmed up.
    /// </summary>
    public async Task RestartRelayAsync()
    {
        await _relay.StopAsync();
        _relay.Dispose();
        _relay = null!; // a failure below must not make DisposeAsync touch a disposed host and mask the original error

        await WaitForLeaderCountAsync(0, "the old relay's leader session is gone");

        _relay = BuildRelay();
        await _relay.StartAsync();

        await WaitForLeaderCountAsync(1, "the new relay holds leadership");
    }

    /// <summary>
    /// Waits until exactly <paramref name="expected"/> sessions of <c>svc_relay</c> hold the relay's
    /// advisory lock (<c>hashtext('nexus-relay')</c>, split into classid/objid as
    /// <c>PostgresRelayLease.ConfirmLockSql</c> does).
    /// </summary>
    private async Task WaitForLeaderCountAsync(int expected, string what)
    {
        const string Sql =
            """
            SELECT count(*)
            FROM pg_catalog.pg_locks l
            JOIN pg_catalog.pg_stat_activity a ON a.pid = l.pid,
                 (SELECT pg_catalog.hashtext('nexus-relay')::bigint AS k) key
            WHERE l.locktype = 'advisory' AND l.granted AND l.objsubid = 1
              AND l.database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = pg_catalog.current_database())
              AND l.classid = ((key.k >> 32) & 4294967295)::oid
              AND l.objid = (key.k & 4294967295)::oid
              AND a.usename = 'svc_relay'
            """;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (await Database.ScalarAsync<long>(Sql) != expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for: " + what);
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// The DI wiring of a consuming service host (AddNexusApplication + AddNexusPersistence +
    /// AddNexusInbox) on the real Postgres as <c>svc_operations</c>.
    /// </summary>
    public ServiceProvider BuildConsumerHost()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddNexusApplication();
        services.AddSingleton(Database.GetServiceDataSource(ConsumerRole));
        services.AddSingleton<IEventSchemaProvider>(new FileSystemEventSchemaProvider(RepoRoot.Find("contracts/events/_test")));
        services.AddNexusPersistence();
        services.AddNexusInbox();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Nexus:Database:ConnectionString", Database.GetConnectionString("svc_platform"));
    }

    /// <summary>(name, absolute .avsc path) of every topic in <c>topics.yaml</c> (flat <c>- name:</c> / <c>schema:</c> entries).</summary>
    private static List<(string Topic, string SchemaPath)> ReadTopics()
    {
        var topics = new List<(string, string)>();
        string? name = null;
        foreach (var line in File.ReadLines(RepoRoot.Find("contracts/events/topics.yaml")))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- name:", StringComparison.Ordinal))
            {
                name = trimmed["- name:".Length..].Trim();
            }
            else if (trimmed.StartsWith("schema:", StringComparison.Ordinal) && name is not null)
            {
                topics.Add((name, RepoRoot.Find(trimmed["schema:".Length..].Trim())));
                name = null;
            }
        }

        return topics;
    }

    private IHost BuildRelay()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<RelayOptions>(options =>
        {
            // Everything else is the production default (PollIntervalMs = 500, ...).
            options.ConnectionString = Database.GetConnectionString("svc_relay");
            options.KafkaBootstrapServers = Kafka.BootstrapServers;
            options.SchemaRegistryUrl = Kafka.SchemaRegistryUrl;
            options.ContractsDirectory = RepoRoot.Find("contracts/events");
        });
        builder.Services.AddOutboxRelay();
        return builder.Build();
    }

    /// <summary>
    /// Test-only table the consumer's effect writes to, created at run time in the throw-away
    /// container (never a migration), as the owner, with grants for the consumer role only.
    /// </summary>
    private async Task CreateEffectTableAsync()
    {
        await using var connection = await Database.OpenAsOwnerAsync();
        await using var command = new NpgsqlCommand(
            $"""
            CREATE SCHEMA IF NOT EXISTS {EffectSchema};
            CREATE TABLE IF NOT EXISTS {EffectSchema}.effect_row (
              id          uuid PRIMARY KEY,
              consumer    text NOT NULL,
              event_id    uuid NOT NULL,
              entity_id   uuid NOT NULL
            );
            REVOKE ALL ON SCHEMA {EffectSchema} FROM PUBLIC;
            GRANT USAGE ON SCHEMA {EffectSchema} TO {ConsumerRole};
            GRANT SELECT, INSERT ON {EffectSchema}.effect_row TO {ConsumerRole};
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
