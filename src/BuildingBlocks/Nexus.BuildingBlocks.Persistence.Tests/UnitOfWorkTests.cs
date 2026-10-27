using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Avro;
using Avro.Generic;
using Avro.IO;
using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Persistence.Tests;

public sealed class UnitOfWorkTests(PersistenceDatabaseFixture db) : IAsyncLifetime
{
    private readonly ServiceDataSource _dataSource = db.CreateServiceDataSource();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Guid _correlationId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    // (a) + (e): business row and outbox row commit together; svc_platform inserts into the
    // outbox with INSERT only (no RETURNING, no read-back).
    [Fact]
    public async Task Commit_PersistsBusinessRowAndOutboxRowTogether_AsInsertOnlyRole()
    {
        var widgetId = Guid.CreateVersion7();
        var legalEntityId = Guid.CreateVersion7();
        var causationId = Guid.CreateVersion7();

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId, causationId))
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = Widget.Create(widgetId, "draft");
            ctx.Widgets.Add(widget);
            widget.Rename("final", 12.5m, new DateTimeOffset(2026, 10, 23, 10, 15, 30, TimeSpan.Zero), legalEntityId);
            await uow.CommitAsync(Ct);

            widget.DomainEvents.ShouldBeEmpty(); // cleared after a successful commit
        }

        (await db.ScalarAsync<string>("SELECT name FROM uow_test.widget WHERE id = $1", widgetId)).ShouldBe("final");

        await using var cmd = db.Admin.CreateCommand(
            "SELECT topic, aggregate_type, aggregate_version, event_type, schema_version, headers::text, payload::text, " +
            "status, attempts, published_at IS NULL, created_at FROM outbox.message WHERE aggregate_id = $1");
        cmd.Parameters.Add(new NpgsqlParameter { Value = widgetId });
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        reader.GetString(0).ShouldBe("nexus.test.widget.renamed");
        reader.GetString(1).ShouldBe("widget");
        reader.GetInt64(2).ShouldBe(1);
        reader.GetString(3).ShouldBe("test.widget.renamed");
        reader.GetInt32(4).ShouldBe(1);

        using var headers = JsonDocument.Parse(reader.GetString(5));
        headers.RootElement.GetProperty(OutboxHeaders.ActorUserId).GetGuid().ShouldBe(_userId);
        headers.RootElement.GetProperty(OutboxHeaders.CorrelationId).GetGuid().ShouldBe(_correlationId);
        headers.RootElement.GetProperty(OutboxHeaders.CausationId).GetGuid().ShouldBe(causationId);
        headers.RootElement.GetProperty(OutboxHeaders.LegalEntityId).GetGuid().ShouldBe(legalEntityId);
        var occurredAt = DateTimeOffset.Parse(headers.RootElement.GetProperty(OutboxHeaders.OccurredAt).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        using var payload = JsonDocument.Parse(reader.GetString(6));
        payload.RootElement.GetProperty("widgetId").GetString().ShouldBe(widgetId.ToString());
        payload.RootElement.GetProperty("newName").GetString().ShouldBe("final");
        payload.RootElement.GetProperty("renamedAt").GetInt64()
            .ShouldBe(new DateTimeOffset(2026, 10, 23, 10, 15, 30, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000);
        payload.RootElement.GetProperty("labels").GetProperty("de").GetString().ShouldBe("final");
        payload.RootElement.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        payload.RootElement.TryGetProperty("aggregateType", out _).ShouldBeFalse(); // envelope stays out of the payload

        reader.GetString(7).ShouldBe("PENDING");
        reader.GetInt32(8).ShouldBe(0);
        reader.GetBoolean(9).ShouldBeTrue();
        reader.GetFieldValue<DateTimeOffset>(10).ShouldBe(occurredAt);
        (await reader.ReadAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task ServiceRole_HasNoSelectOnOutbox_SoTheWriterMustNotReadBack()
    {
        await using var connection = await _dataSource.OpenConnectionAsync(Ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM outbox.message", connection);
        var ex = await Should.ThrowAsync<PostgresException>(() => cmd.ExecuteScalarAsync(Ct));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    // (b) failure after the outbox write and before COMMIT: nothing survives.
    [Fact]
    public async Task FailureAfterOutboxWrite_BeforeCommit_RollsBackBusinessAndOutboxRows()
    {
        var widgetId = Guid.CreateVersion7();

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId, decorateWriter: w => new ThrowAfterWriteOutboxWriter(w)))
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = Widget.Create(widgetId, "draft");
            ctx.Widgets.Add(widget);
            widget.Rename("final", 1m, DateTimeOffset.UtcNow);

            await Should.ThrowAsync<SimulatedFailureException>(() => uow.CommitAsync(Ct));
            uow.IsActive.ShouldBeFalse();
            widget.DomainEvents.Count.ShouldBe(1); // not cleared: nothing was committed
        }

        (await CountWidgets(widgetId)).ShouldBe(0);
        (await CountOutbox(widgetId)).ShouldBe(0);
    }

    [Fact]
    public async Task DisposeWithoutCommit_RollsBack()
    {
        var widgetId = Guid.CreateVersion7();

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var cmd = new NpgsqlCommand("INSERT INTO uow_test.widget (id, name) VALUES ($1, 'x')", uow.Connection, uow.Transaction);
            cmd.Parameters.Add(new NpgsqlParameter { Value = widgetId });
            await cmd.ExecuteNonQueryAsync(Ct);
            uow.AddEvent(new WidgetDeleted(widgetId), 1);

            // handler throws -> scope disposes the unit of work without committing
        }

        (await CountWidgets(widgetId)).ShouldBe(0);
        (await CountOutbox(widgetId)).ShouldBe(0);
    }

    // (c) EV-R2: a payload that violates its .avsc aborts the whole transaction.
    [Fact]
    public async Task InvalidPayload_AbortsTheTransaction()
    {
        var widgetId = Guid.CreateVersion7();

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = Widget.Create(widgetId, "draft");
            ctx.Widgets.Add(widget);
            widget.Rename("ok", 1m, DateTimeOffset.UtcNow); // valid event in the same unit of work
            widget.RenameWithInvalidPayload();

            var ex = await Should.ThrowAsync<EventPayloadSchemaException>(() => uow.CommitAsync(Ct));
            ex.Message.ShouldContain("newName");
        }

        (await CountWidgets(widgetId)).ShouldBe(0);
        (await CountOutbox(widgetId)).ShouldBe(0);
    }

    [Fact]
    public async Task EventWithoutLocalSchema_AbortsTheTransaction()
    {
        var widgetId = Guid.CreateVersion7();

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var cmd = new NpgsqlCommand("INSERT INTO uow_test.widget (id, name) VALUES ($1, 'x')", uow.Connection, uow.Transaction);
            cmd.Parameters.Add(new NpgsqlParameter { Value = widgetId });
            await cmd.ExecuteNonQueryAsync(Ct);
            uow.AddEvent(new UnknownEvent(widgetId), 1);

            await Should.ThrowAsync<EventSchemaNotFoundException>(() => uow.CommitAsync(Ct));
        }

        (await CountWidgets(widgetId)).ShouldBe(0);
    }

    // (d) app.current_user_id / app.correlation_id are transaction-local.
    [Fact]
    public async Task SessionContext_IsSetInsideTheTransaction_AndGoneAfterIt()
    {
        await using var singleConnection = db.CreateServiceDataSource(maxPoolSize: 1);
        int insidePid;

        await using (var uow = db.CreateUnitOfWork(singleConnection, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT current_setting('app.current_user_id'), current_setting('app.correlation_id'), pg_backend_pid()",
                uow.Connection,
                uow.Transaction);
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            await reader.ReadAsync(Ct);
            reader.GetString(0).ShouldBe(_userId.ToString());
            reader.GetString(1).ShouldBe(_correlationId.ToString());
            insidePid = reader.GetInt32(2);
            await reader.CloseAsync();
            await uow.CommitAsync(Ct);
        }

        // Same physical connection (pool of one), outside any unit of work.
        await using var connection = await singleConnection.OpenConnectionAsync(Ct);
        await using var after = new NpgsqlCommand(
            "SELECT coalesce(current_setting('app.current_user_id', true), ''), coalesce(current_setting('app.correlation_id', true), ''), pg_backend_pid()",
            connection);
        await using var afterReader = await after.ExecuteReaderAsync(Ct);
        await afterReader.ReadAsync(Ct);
        afterReader.GetString(0).ShouldBeEmpty();
        afterReader.GetString(1).ShouldBeEmpty();
        afterReader.GetInt32(2).ShouldBe(insidePid);
    }

    [Fact]
    public async Task Begin_RequiresAuthenticatedUserAndCorrelation()
    {
        await using var anonymous = new UnitOfWork(
            _dataSource,
            new TestCurrentUser(Guid.CreateVersion7(), isAuthenticated: false),
            new CorrelationContext { CorrelationId = _correlationId },
            new OutboxWriter(db.SchemaProvider, new TestIdGenerator(), TimeProvider.System));
        await Should.ThrowAsync<InvalidOperationException>(() => anonymous.BeginAsync(Ct));

        await using var uncorrelated = db.CreateUnitOfWork(_dataSource, _userId, Guid.Empty);
        await Should.ThrowAsync<InvalidOperationException>(() => uncorrelated.BeginAsync(Ct));
    }

    // BE-R1: an authenticated caller not (yet) resolved to an app_user (UserId null, e.g. an
    // unprovisioned oid) or carrying an empty id must never open an RLS session.
    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Begin_RejectsAuthenticatedUserWithoutUserId(string? userId)
    {
        await using var unresolved = new UnitOfWork(
            _dataSource,
            new TestCurrentUser(userId is null ? null : Guid.Parse(userId, CultureInfo.InvariantCulture)),
            new CorrelationContext { CorrelationId = _correlationId },
            new OutboxWriter(db.SchemaProvider, new TestIdGenerator(), TimeProvider.System));

        await Should.ThrowAsync<InvalidOperationException>(() => unresolved.BeginAsync(Ct));
    }

    [Fact]
    public async Task UnitOfWork_IsSingleUse()
    {
        await using var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId);
        await uow.BeginAsync(Ct);
        await uow.CommitAsync(Ct);

        Should.Throw<InvalidOperationException>(() => uow.Connection);
        await Should.ThrowAsync<InvalidOperationException>(() => uow.CommitAsync(Ct));
        await Should.ThrowAsync<InvalidOperationException>(() => uow.BeginAsync(Ct));
    }

    // EV-R3 support: rows of one unit of work come out of ORDER BY (created_at, id) in raise order.
    [Fact]
    public async Task ManyEventsOfOneAggregate_KeepRaiseOrderUnderRelayOrdering()
    {
        var widgetId = Guid.CreateVersion7();

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = Widget.Create(widgetId, "n0");
            ctx.Widgets.Add(widget);
            for (var i = 1; i <= 40; i++)
            {
                widget.Rename($"n{i}", 1m, DateTimeOffset.UtcNow);
            }

            await uow.CommitAsync(Ct);
        }

        await using var cmd = db.Admin.CreateCommand(
            "SELECT aggregate_version, payload FROM outbox.message WHERE aggregate_id = $1 ORDER BY created_at, id");
        cmd.Parameters.Add(new NpgsqlParameter { Value = widgetId });
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        var expected = 1L;
        while (await reader.ReadAsync(Ct))
        {
            reader.GetInt64(0).ShouldBe(expected);
            using var payload = JsonDocument.Parse(reader.GetString(1)); // payload is text (ADR-0013)
            payload.RootElement.GetProperty("newName").GetString().ShouldBe($"n{expected}");
            expected++;
        }

        expected.ShouldBe(41);
    }

    [Fact]
    public async Task GuidSortOrder_MatchesPostgresUuidOrder()
    {
        var guids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
        var dotnet = guids.Order().ToArray();

        await using var cmd = db.Admin.CreateCommand("SELECT g FROM unnest($1::uuid[]) AS g ORDER BY g");
        cmd.Parameters.Add(new NpgsqlParameter { Value = guids });
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        var postgres = new List<Guid>();
        while (await reader.ReadAsync(Ct))
        {
            postgres.Add(reader.GetGuid(0));
        }

        postgres.ShouldBe(dotnet);
    }

    [Fact]
    public async Task DeletedAggregate_StillHasItsEventsWritten()
    {
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name) VALUES ('{widgetId}', 'doomed')");

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = await ctx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);
            widget.MarkDeleted();
            ctx.Widgets.Remove(widget);
            await uow.CommitAsync(Ct);
        }

        (await CountWidgets(widgetId)).ShouldBe(0);
        (await db.ScalarAsync<string>("SELECT event_type FROM outbox.message WHERE aggregate_id = $1", widgetId))
            .ShouldBe("test.widget.deleted");
    }

    [Fact]
    public async Task UntrackedAggregate_RegisteredExplicitly_IsWrittenOnce()
    {
        var widgetId = Guid.CreateVersion7();

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = Widget.Create(widgetId, "a");
            ctx.Widgets.Add(widget);
            widget.Rename("b", 2m, DateTimeOffset.UtcNow);
            uow.AddEventsFrom(widget); // also tracked: must not duplicate
            await uow.CommitAsync(Ct);
        }

        (await CountOutbox(widgetId)).ShouldBe(1);
    }

    [Fact]
    public async Task DirectSaveChanges_IsForbidden_BE_R2()
    {
        await using var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId);
        await uow.BeginAsync(Ct);
        await using var ctx = new WidgetDbContext(uow);
        ctx.Widgets.Add(Widget.Create(Guid.CreateVersion7(), "x"));

        Should.Throw<InvalidOperationException>(() => ctx.SaveChanges()).Message.ShouldContain("BE-R2");
        await Should.ThrowAsync<InvalidOperationException>(() => ctx.SaveChangesAsync(Ct));
    }

    // BE-R3: xmin is a concurrency token on every entity without any per-entity configuration.
    [Fact]
    public async Task ConcurrentUpdate_OfSameRow_FailsWithConcurrencyException_BE_R3()
    {
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name) VALUES ('{widgetId}', 'v0')");

        await using var first = db.CreateUnitOfWork(_dataSource, _userId, _correlationId);
        await first.BeginAsync(Ct);
        await using var firstCtx = new WidgetDbContext(first);
        var stale = await firstCtx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);

        // Another unit of work updates and commits in between.
        await using (var second = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await second.BeginAsync(Ct);
            await using var secondCtx = new WidgetDbContext(second);
            var fresh = await secondCtx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);
            fresh.Rename("v1", 1m, DateTimeOffset.UtcNow);
            await second.CommitAsync(Ct);
        }

        stale.Rename("v2", 2m, DateTimeOffset.UtcNow);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => first.CommitAsync(Ct));

        (await db.ScalarAsync<string>("SELECT name FROM uow_test.widget WHERE id = $1", widgetId)).ShouldBe("v1");
        (await CountOutbox(widgetId)).ShouldBe(1);
    }

    [Fact]
    public void Model_MapsXminAndIgnoresAggregateBookkeeping()
    {
        // Model building needs no live connection.
        var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId);
        using var ctx = new WidgetDbContextForModel(uow);
        var entity = ctx.Model.FindEntityType(typeof(Widget))!;

        var xmin = entity.FindProperty("xmin")!;
        xmin.IsConcurrencyToken.ShouldBeTrue();
        xmin.GetColumnName().ShouldBe("xmin");
        xmin.GetColumnType().ShouldBe("xid");
        entity.FindProperty(nameof(Widget.AggregateVersion)).ShouldBeNull();
        entity.FindNavigation(nameof(Widget.DomainEvents)).ShouldBeNull();
        entity.FindProperty(nameof(Widget.Name))!.GetColumnName().ShouldBe("name");
    }

    /// <summary>
    /// ADR-0013: Avro's JSON encoding writes decimal bytes as \u00XX escapes. At scale 4,
    /// 16.0000 = 160000 = 0x02_71_00 and 256.0000 = 2560000 = 0x27_10_00 both contain a zero
    /// byte, i.e. a \u0000 escape, which jsonb rejected (22P05) and aborted the unit of work.
    /// outbox.message.payload is text now: the unit of work commits, the outbox row exists, and
    /// the stored payload decodes back (with the event's own .avsc) to the exact decimal.
    /// </summary>
    [Theory]
    [InlineData("16.0000")] // ordinary amounts hit this, not just edge cases
    [InlineData("256.0000")]
    public async Task DecimalWithZeroByte_IsStoredInTextPayload_AndReadsBackExactly(string price)
    {
        var widgetId = Guid.CreateVersion7();
        var expected = decimal.Parse(price, CultureInfo.InvariantCulture);

        await using (var uow = db.CreateUnitOfWork(_dataSource, _userId, _correlationId))
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = Widget.Create(widgetId, "x");
            ctx.Widgets.Add(widget);
            widget.Rename("y", expected, DateTimeOffset.UtcNow);
            await uow.CommitAsync(Ct);
        }

        (await CountWidgets(widgetId)).ShouldBe(1);
        (await CountOutbox(widgetId)).ShouldBe(1);

        var stored = await db.ScalarAsync<string>("SELECT payload FROM outbox.message WHERE aggregate_id = $1", widgetId);
        stored.ShouldNotBeNull();
        stored.ShouldContain("\\u0000"); // the escape jsonb refused: proves this case is exercised
        stored.ShouldNotContain("\0"); // escaped, never a raw NUL (text cannot hold one either)

        // Raw bytes: the JSON string's chars are the two's-complement big-endian unscaled value.
        using (var json = JsonDocument.Parse(stored))
        {
            var bytes = json.RootElement.GetProperty("price").GetString()!.Select(c => checked((byte)c)).ToArray();
            bytes.ShouldContain((byte)0);
            new BigInteger(bytes, isUnsigned: false, isBigEndian: true)
                .ShouldBe(new BigInteger(expected * 10_000m));
        }

        // Round trip through Avro's own JSON decoder, as the relay (step 0.7) will read it.
        var schema = db.SchemaProvider.GetSchema(new WidgetRenamed(widgetId, "y", DateTimeOffset.UtcNow, expected, new Dictionary<string, string>(), null));
        var record = new GenericDatumReader<GenericRecord>(schema, schema).Read(null!, new JsonDecoder(schema, stored));
        var avroPrice = (AvroDecimal)record["price"];
        avroPrice.Scale.ShouldBe(4);
        avroPrice.UnscaledValue.ShouldBe(new BigInteger(expected * 10_000m)); // exact: 160000 / 2560000 at scale 4
        ((decimal)avroPrice).ShouldBe(expected);
    }

    private async Task<long> CountWidgets(Guid id) =>
        await db.ScalarAsync<long>("SELECT count(*) FROM uow_test.widget WHERE id = $1", id);

    private async Task<long> CountOutbox(Guid aggregateId) =>
        await db.ScalarAsync<long>("SELECT count(*) FROM outbox.message WHERE aggregate_id = $1", aggregateId);

    private sealed record UnknownEvent(Guid WidgetId) : Domain.IDomainEvent
    {
        public string AggregateType => "widget";

        public Guid AggregateId => WidgetId;

        public string EventType => "test.widget.unknown";

        public int SchemaVersion => 1;

        public string Topic => "nexus.test.widget.unknown";

        public Guid? LegalEntityId => null;
    }

    /// <summary>
    /// Same model as <see cref="WidgetDbContext"/>. Built from a unit of work that never called
    /// <c>BeginAsync</c>, so <see cref="DbContextBase.OnConfiguring"/> takes its no-connection,
    /// model-inspection-only path on its own -- no override needed here.
    /// </summary>
    private sealed class WidgetDbContextForModel(IUnitOfWork uow) : DbContextBase(uow)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Widget>(b =>
            {
                b.ToTable("widget", "uow_test");
                b.HasKey(w => w.Id);
            });
    }
}
