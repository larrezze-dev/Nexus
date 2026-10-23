using System.Text.Json;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Npgsql;
using Shouldly;

[assembly: AssemblyFixture(typeof(Nexus.BuildingBlocks.Testing.PostgresFixture))]

namespace Nexus.BuildingBlocks.Testing.Tests;

/// <summary>
/// Proves <see cref="PostgresFixture"/> actually ran <c>db/bootstrap/00_roles.sql</c> and Flyway
/// (both <c>db/migrations</c> and <c>db/test-migrations</c>) for real, and that a genuine
/// <see cref="UnitOfWork"/> built on top of it writes to <c>outbox.message</c> with its payload
/// validated against the real <c>contracts/events/_test/</c> Avro contract.
/// </summary>
public sealed class PostgresFixtureTests(PostgresFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bootstrap_CreatedAllTenRoles_WithSolverAloneHavingBypassRls()
    {
        (await db.ScalarAsync<bool>("SELECT rolbypassrls FROM pg_roles WHERE rolname = 'svc_solver'")).ShouldBeTrue();
        (await db.ScalarAsync<bool>("SELECT rolbypassrls FROM pg_roles WHERE rolname = 'svc_platform'")).ShouldBeFalse();
        (await db.ScalarAsync<bool>("SELECT rolbypassrls FROM pg_roles WHERE rolname = 'nexus_migrator'")).ShouldBeFalse();

        var roleCount = await db.ScalarAsync<long>(
            "SELECT count(*) FROM pg_roles WHERE rolname IN (" +
            "'nexus_migrator','svc_platform','svc_catalog','svc_operations','svc_commercial'," +
            "'svc_resources','svc_controlling','svc_field','svc_relay','svc_solver')");
        roleCount.ShouldBe(10);
    }

    [Fact]
    public async Task Flyway_AppliedBothMigrationsAndTestMigrationsLocations()
    {
        // db/migrations: the real outbox schema (V202610231001__infra__outbox_inbox.sql).
        (await db.ScalarAsync<bool>("SELECT to_regclass('outbox.message') IS NOT NULL")).ShouldBeTrue();

        // db/test-migrations: test.append_only_probe only exists if that second location ran too.
        (await db.ScalarAsync<bool>("SELECT to_regclass('test.append_only_probe') IS NOT NULL")).ShouldBeTrue();

        // afterConnect.sql's `SET ROLE nexus_owner` applied to every connection Flyway opened,
        // including the one that created the test-only probe table (DBC-6).
        (await db.ScalarAsync<string>(
            "SELECT pg_get_userbyid(relowner) FROM pg_class WHERE oid = 'test.append_only_probe'::regclass"))
            .ShouldBe("nexus_owner");
    }

    [Fact]
    public async Task UnitOfWork_EmitsTestEvent_WrittenToOutbox_ValidatedAgainstRealAvscContract()
    {
        var schemaProvider = new FileSystemEventSchemaProvider(RepoRoot.Find("contracts/events/_test"));
        var writer = new OutboxWriter(schemaProvider, new TestIdGenerator(), TimeProvider.System);
        var dataSource = db.GetServiceDataSource("svc_platform");
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();

        var testEventId = Guid.CreateVersion7();
        await using (var uow = new UnitOfWork(dataSource, new TestCurrentUser(userId), new CorrelationContext { CorrelationId = correlationId }, writer))
        {
            await uow.BeginAsync(Ct);
            uow.AddEvent(new TestEvent(testEventId, "hello"), aggregateVersion: 1);
            await uow.CommitAsync(Ct);
        }

        await using var command = db.Admin.CreateCommand(
            "SELECT topic, aggregate_type, event_type, schema_version, payload::text, status FROM outbox.message WHERE aggregate_id = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = testEventId });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();

        reader.GetString(0).ShouldBe("nexus.test.test-event.created");
        reader.GetString(1).ShouldBe("test-event");
        reader.GetString(2).ShouldBe("test.test-event.created");
        reader.GetInt32(3).ShouldBe(1);
        reader.GetString(5).ShouldBe("PENDING");

        using var payload = JsonDocument.Parse(reader.GetString(4));
        payload.RootElement.GetProperty("testEventId").GetString().ShouldBe(testEventId.ToString());
        payload.RootElement.GetProperty("name").GetString().ShouldBe("hello");

        (await reader.ReadAsync(Ct)).ShouldBeFalse();
    }
}
