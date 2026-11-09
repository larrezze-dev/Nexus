using System.Net.Http.Headers;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.AspNetCore.Hosting;
using Nexus.BuildingBlocks.Testing;
using Testcontainers.ClickHouse;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <see cref="PlatformApiFixture"/> plus a disposable ClickHouse (same image tag as
/// <c>infra/compose</c>) holding the real <c>db/clickhouse/V202611082100__base.sql</c>, wired in
/// through <c>Nexus:ClickHouse:ConnectionString</c>. Never touches the developer's
/// <c>nexus-*</c> containers.
/// </summary>
public sealed class AuditApiFixture : WebAppFixture<Program>, IAsyncLifetime
{
    private ClickHouseContainer _clickHouse = null!;

    public OrgFixture Org { get; } = new();

    public PostgresFixture Database => Org.Database;

    public string ClickHouseConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Org.InitializeAsync();

        _clickHouse = new ClickHouseBuilder("clickhouse/clickhouse-server:26.8.34.13").Build();
        await _clickHouse.StartAsync();
        ClickHouseConnectionString = _clickHouse.GetConnectionString();

        var ddl = await File.ReadAllTextAsync(Path.Combine(RepoRoot.Find("db/clickhouse"), "V202611082100__base.sql"));
        await using var connection = await OpenClickHouseAsync();
        foreach (var fragment in ddl.Split(';'))
        {
            var meaningful = fragment.Split('\n').Any(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith("--", StringComparison.Ordinal));
            if (meaningful)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = fragment;
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public HttpClient CreateClientFor(string oid)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuer.IssueToken(oid: oid));
        return client;
    }

    public async Task<ClickHouseConnection> OpenClickHouseAsync()
    {
        var connection = new ClickHouseConnection(ClickHouseConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Inserts the rows into <c>nexus_analytics.event_log</c> in one insert.</summary>
    public async Task InsertEventsAsync(params EventLogRow[] rows)
    {
        await using var connection = await OpenClickHouseAsync();
        using var bulk = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = "nexus_analytics.event_log",
            ColumnNames =
            [
                "legal_entity_id", "topic", "event_id", "event_type", "aggregate_type", "aggregate_id", "aggregate_version",
                "occurred_at", "actor_user_id", "correlation_id", "payload_json", "_ingested_at",
            ],
        };
        await bulk.InitAsync();
        await bulk.WriteToServerAsync(rows.Select(r => new object?[]
        {
            r.LegalEntityId, r.Topic, r.EventId, r.EventType, r.AggregateType, r.AggregateId, r.AggregateVersion,
            r.OccurredAt, r.ActorUserId, r.CorrelationId, r.PayloadJson, DateTime.UtcNow,
        }).ToList()!);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_clickHouse is not null)
        {
            await _clickHouse.DisposeAsync();
        }

        await Org.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Nexus:Database:ConnectionString", Database.GetConnectionString("svc_platform"));
        builder.UseSetting("Nexus:ClickHouse:ConnectionString", ClickHouseConnectionString);
    }
}

/// <summary>One <c>event_log</c> row for arranging tests.</summary>
public sealed record EventLogRow(
    Guid? LegalEntityId,
    string AggregateType,
    Guid AggregateId,
    ulong AggregateVersion,
    string PayloadJson,
    Guid EventId,
    string EventType,
    string Topic,
    DateTime OccurredAt,
    Guid ActorUserId,
    Guid CorrelationId)
{
    /// <summary>A row with fresh ids; <paramref name="payloadJson"/> '' for a PII topic.</summary>
    public static EventLogRow Create(Guid? legalEntityId, string aggregateType, Guid aggregateId, ulong version, string payloadJson, Guid actorUserId, string verb = "updated")
    {
        var occurredAt = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc).AddMinutes(version).AddTicks(1230); // .123 ms = 123 µs
        return new EventLogRow(
            legalEntityId,
            aggregateType,
            aggregateId,
            version,
            payloadJson,
            Guid.CreateVersion7(),
            $"org.{aggregateType}.{verb}",
            $"nexus.org.{aggregateType}.{verb}",
            occurredAt,
            actorUserId,
            Guid.CreateVersion7());
    }
}
