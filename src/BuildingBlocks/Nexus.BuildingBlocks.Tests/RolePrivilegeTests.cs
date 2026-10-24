using Nexus.BuildingBlocks.Testing;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// Role privileges (conventions-database.md §2, db/bootstrap/00_roles.sql, outbox grants of
/// V202610231001): a service writes only in its own schemas (ARC-R1), and the relay only
/// reads/marks/purges <c>outbox.message</c>, it never inserts into it.
/// </summary>
public sealed class RolePrivilegeTests(PostgresFixture db)
{
    private const string InsufficientPrivilege = "42501";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ARC-R1: svc_catalog cannot INSERT into a table of another schema (test.append_only_probe)")]
    public async Task SvcCatalog_CannotInsertIntoForeignSchema()
    {
        var id = Guid.CreateVersion7();
        await using var connection = await db.GetServiceDataSource("svc_catalog").OpenConnectionAsync(Ct);
        await using var command = new NpgsqlCommand(
            "INSERT INTO test.append_only_probe (id, created_at, created_by) VALUES ($1, now(), $2)", connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.CreateVersion7() });

        var ex = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        ex.SqlState.ShouldBe(InsufficientPrivilege);
        ex.MessageText.ShouldBe("permission denied for schema test");
        (await db.ScalarAsync<long>("SELECT count(*) FROM test.append_only_probe WHERE id = $1", id)).ShouldBe(0);
    }

    [Fact(DisplayName = "Roles: svc_relay cannot INSERT into outbox.message")]
    public async Task SvcRelay_CannotInsertIntoOutbox()
    {
        var id = Guid.CreateVersion7();
        await using var connection = await db.GetServiceDataSource("svc_relay").OpenConnectionAsync(Ct);

        // Positive control: the relay does reach outbox.message (USAGE + SELECT), so the INSERT
        // below fails on the missing INSERT privilege itself, not on schema access.
        await using (var select = new NpgsqlCommand("SELECT count(*) FROM outbox.message WHERE id = $1", connection))
        {
            select.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
            ((long)(await select.ExecuteScalarAsync(Ct))!).ShouldBe(0);
        }

        await using var insert = new NpgsqlCommand(
            "INSERT INTO outbox.message (id, topic, aggregate_type, aggregate_id, aggregate_version, " +
            "event_type, schema_version, headers, payload, created_at) " +
            "VALUES ($1, 'nexus.test.test-event.created', 'test-event', $1, 1, 'test.test-event.created', 1, '{}'::jsonb, '{}', now())",
            connection);
        insert.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(Ct));

        ex.SqlState.ShouldBe(InsufficientPrivilege);
        ex.MessageText.ShouldBe("permission denied for table message");
        (await db.ScalarAsync<long>("SELECT count(*) FROM outbox.message WHERE id = $1", id)).ShouldBe(0);
    }
}
