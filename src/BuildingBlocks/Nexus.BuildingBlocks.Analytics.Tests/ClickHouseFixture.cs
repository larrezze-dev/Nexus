using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Analytics.Querying;
using Nexus.BuildingBlocks.Testing;
using Testcontainers.ClickHouse;

namespace Nexus.BuildingBlocks.Analytics.Tests;

/// <summary>
/// A disposable ClickHouse (same image tag as <c>infra/compose</c>) with the real
/// <c>db/clickhouse/V202611082100__base.sql</c> applied (split on <c>;</c>, comment-only fragments
/// skipped, as that file documents is safe). Background merges of <c>event_log</c> are stopped so
/// duplicate rows of one event stay in separate parts and the queries must collapse them with
/// <c>FINAL</c>. Never touches the developer's <c>nexus-*</c> containers.
/// </summary>
public sealed class ClickHouseFixture : IAsyncLifetime
{
    private ClickHouseContainer _container = null!;

    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _container = new ClickHouseBuilder("clickhouse/clickhouse-server:26.8.34.13").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        var ddl = await File.ReadAllTextAsync(Path.Combine(RepoRoot.Find("db/clickhouse"), "V202611082100__base.sql"));
        await using var connection = await OpenAsync();
        foreach (var fragment in ddl.Split(';'))
        {
            var meaningful = fragment.Split('\n').Any(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith("--", StringComparison.Ordinal));
            if (meaningful)
            {
                await ExecAsync(connection, fragment);
            }
        }

        await ExecAsync(connection, "SYSTEM STOP MERGES nexus_analytics.event_log");
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task<ClickHouseConnection> OpenAsync()
    {
        var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>A real executor (real connection factory) pointed at this container.</summary>
    internal AnalyticsQueryExecutor CreateExecutor(TimeSpan? queryTimeout = null) =>
        new(CreateConnectionFactory(queryTimeout));

    internal AnalyticsConnectionFactory CreateConnectionFactory(TimeSpan? queryTimeout = null) =>
        new(Options.Create(new NexusAnalyticsOptions
        {
            ConnectionString = ConnectionString,
            QueryTimeout = queryTimeout ?? NexusAnalyticsOptions.DefaultQueryTimeout,
        }));

    /// <summary>
    /// The real factory plus <c>max_block_size = 1</c>, for the slow-query tests: analytic SQL may
    /// not carry a SETTINGS clause, and ClickHouse checks time limits and cancellation between
    /// blocks (and caps sleep at 3 s per block).
    /// </summary>
    internal AnalyticsQueryExecutor CreateSlowQueryExecutor(TimeSpan? queryTimeout = null) =>
        new(new OneRowBlocks(CreateConnectionFactory(queryTimeout)));

    private sealed class OneRowBlocks(IAnalyticsConnectionFactory inner) : IAnalyticsConnectionFactory
    {
        public TimeSpan QueryTimeout => inner.QueryTimeout;

        public ClickHouseConnection Create()
        {
            var connection = inner.Create();
            connection.CustomSettings["max_block_size"] = 1;
            return connection;
        }
    }

    /// <summary>Inserts the rows in ONE insert (one part). Call it twice to get two parts.</summary>
    public async Task InsertAsync(params EventLogRow[] rows)
    {
        await using var connection = await OpenAsync();
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
            r.OccurredAt, r.ActorUserId, r.CorrelationId, r.PayloadJson, r.IngestedAt,
        }).ToList()!);
    }

    public async Task<ulong> RawCountAsync(string where)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count() FROM nexus_analytics.event_log WHERE " + where;
        return (ulong)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecAsync(ClickHouseConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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
    Guid CorrelationId,
    DateTime IngestedAt)
{
    /// <summary>A row with fresh ids; <paramref name="payloadJson"/> '' for a PII topic.</summary>
    public static EventLogRow Create(Guid? legalEntityId, string aggregateType, Guid aggregateId, ulong version, string payloadJson, string verb = "updated")
    {
        var occurredAt = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc).AddMinutes(version).AddTicks(1230); // 123 µs
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
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTime.UtcNow);
    }
}
