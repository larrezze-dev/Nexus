using Nexus.BuildingBlocks.Testing;
using Nexus.BuildingBlocks.Tests.Support;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// Append-only tables (conventions-database.md §6, conventions-testing.md §3 "Tabla append-only"):
/// UPDATE and DELETE fail with the service role and with the owner, on
/// <c>test.append_only_probe</c> (db/test-migrations).
///
/// <para>
/// The two failures have different causes, on purpose (ficha 0.3, "Verificación de Luciano" 2):
/// the service role is stopped by privileges before the row trigger can fire (§6 revokes
/// UPDATE/DELETE from it too, and it holds nothing on schema <c>test</c>: defense in depth),
/// while <c>nexus_owner</c> does hold the privilege and is stopped by
/// <c>infra.forbid_mutation()</c> ("the trigger also covers the owner").
/// </para>
///
/// <para>
/// Each test targets a row that really exists: a BEFORE ROW trigger only fires for matching
/// rows, so an UPDATE/DELETE on an empty table would "succeed" and prove nothing.
/// </para>
/// </summary>
public sealed class ForbidMutationTests(PostgresFixture db)
{
    private const string ServiceRole = "svc_platform";
    private const string InsufficientPrivilege = "42501";
    private const string RaiseException = "P0001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "forbid_mutation: UPDATE on append-only table fails for a service role (permission denied for schema test)")]
    public async Task Update_AsServiceRole_IsDeniedByPrivileges()
    {
        var (id, createdBy) = await InsertProbeRowAsync();

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsServiceRoleAsync("UPDATE test.append_only_probe SET created_by = $2 WHERE id = $1", id, Guid.CreateVersion7()));

        ex.SqlState.ShouldBe(InsufficientPrivilege);
        ex.MessageText.ShouldBe("permission denied for schema test");
        await AssertProbeRowUnchangedAsync(id, createdBy);
    }

    [Fact(DisplayName = "forbid_mutation: DELETE on append-only table fails for a service role (permission denied for schema test)")]
    public async Task Delete_AsServiceRole_IsDeniedByPrivileges()
    {
        var (id, createdBy) = await InsertProbeRowAsync();

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsServiceRoleAsync("DELETE FROM test.append_only_probe WHERE id = $1", id));

        ex.SqlState.ShouldBe(InsufficientPrivilege);
        ex.MessageText.ShouldBe("permission denied for schema test");
        await AssertProbeRowUnchangedAsync(id, createdBy);
    }

    [Fact(DisplayName = "forbid_mutation: UPDATE on append-only table fails for nexus_owner (append-only table, trigger fires)")]
    public async Task Update_AsOwner_IsBlockedByTrigger()
    {
        var (id, createdBy) = await InsertProbeRowAsync();

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsOwnerAsync("UPDATE test.append_only_probe SET created_by = $2 WHERE id = $1", id, Guid.CreateVersion7()));

        ex.SqlState.ShouldBe(RaiseException);
        ex.MessageText.ShouldBe("append-only table append_only_probe");
        await AssertProbeRowUnchangedAsync(id, createdBy);
    }

    [Fact(DisplayName = "forbid_mutation: DELETE on append-only table fails for nexus_owner (append-only table, trigger fires)")]
    public async Task Delete_AsOwner_IsBlockedByTrigger()
    {
        var (id, createdBy) = await InsertProbeRowAsync();

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsOwnerAsync("DELETE FROM test.append_only_probe WHERE id = $1", id));

        ex.SqlState.ShouldBe(RaiseException);
        ex.MessageText.ShouldBe("append-only table append_only_probe");
        await AssertProbeRowUnchangedAsync(id, createdBy);
    }

    [Fact(DisplayName = "forbid_mutation: the owner-side probe really runs as nexus_owner, which holds UPDATE and DELETE")]
    public async Task OwnerConnection_IsNexusOwner_WithUpdateAndDeletePrivileges()
    {
        // Guards the two owner tests above against passing for the wrong reason: if the owner
        // connection lacked the privilege, they would fail on 42501, not on the trigger.
        await using var connection = await RuntimeTestSchema.OpenAsOwnerAsync(db, Ct);
        await using var command = new NpgsqlCommand(
            "SELECT current_user, " +
            "has_table_privilege('test.append_only_probe', 'UPDATE'), " +
            "has_table_privilege('test.append_only_probe', 'DELETE')",
            connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        reader.GetString(0).ShouldBe("nexus_owner");
        reader.GetBoolean(1).ShouldBeTrue();
        reader.GetBoolean(2).ShouldBeTrue();
    }

    private async Task<(Guid Id, Guid CreatedBy)> InsertProbeRowAsync()
    {
        var id = Guid.CreateVersion7();
        var createdBy = Guid.CreateVersion7();
        await ExecuteAsOwnerAsync(
            "INSERT INTO test.append_only_probe (id, created_at, created_by) VALUES ($1, now(), $2)", id, createdBy);
        return (id, createdBy);
    }

    private async Task AssertProbeRowUnchangedAsync(Guid id, Guid createdBy)
    {
        var stored = await db.ScalarAsync<Guid?>("SELECT created_by FROM test.append_only_probe WHERE id = $1", id);
        stored.ShouldBe(createdBy);
    }

    private async Task ExecuteAsServiceRoleAsync(string sql, params Guid[] parameters)
    {
        await using var connection = await db.GetServiceDataSource(ServiceRole).OpenConnectionAsync(Ct);
        await ExecuteAsync(connection, sql, parameters);
    }

    private async Task ExecuteAsOwnerAsync(string sql, params Guid[] parameters)
    {
        await using var connection = await RuntimeTestSchema.OpenAsOwnerAsync(db, Ct);
        await ExecuteAsync(connection, sql, parameters);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, Guid[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = parameter });
        }

        await command.ExecuteNonQueryAsync(Ct);
    }
}
