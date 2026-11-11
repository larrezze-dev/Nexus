using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Application;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Persistence.Tests;

/// <summary>
/// ADR-0019: <c>AggregateRoot.AggregateVersion</c> is persisted in <c>aggregate_version</c> and is
/// an EF Core concurrency token, so <c>nexus-aggregate-version</c> grows across commands and two
/// concurrent commands can never emit the same version. Real PostgreSQL, real connections.
/// </summary>
public sealed class AggregateVersionTests(PersistenceDatabaseFixture db) : IAsyncLifetime
{
    private readonly ServiceDataSource _dataSource = db.CreateServiceDataSource();
    private readonly Guid _userId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    [Fact(DisplayName = "ADR-0019: three successive commands on one aggregate emit versions 1, 2, 3 and persist the last")]
    public async Task SuccessiveUnitsOfWork_EmitVersions1To3_AndPersistTheLast()
    {
        var widgetId = Guid.CreateVersion7();

        // 1: create + raise. The INSERT must carry the version (the column default is 0).
        await using (var uow = NewUnitOfWork())
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = Widget.Create(widgetId, "v0");
            ctx.Widgets.Add(widget);
            widget.Rename("v1", 1m, RoundedSystemTimeProvider.Instance.GetUtcNow());
            await uow.CommitAsync(Ct);
        }

        (await StoredVersion(widgetId)).ShouldBe(1);

        // 2 and 3: load (the persisted version comes back), mutate, commit.
        for (var expectedLoaded = 1L; expectedLoaded <= 2; expectedLoaded++)
        {
            await RenameInOwnUnitOfWork(widgetId, $"v{expectedLoaded + 1}", expectedLoaded);
            (await StoredVersion(widgetId)).ShouldBe(expectedLoaded + 1);
        }

        (await OutboxVersions(widgetId)).ShouldBe([1L, 2L, 3L]);
    }

    [Fact(DisplayName = "ADR-0019 §4: after the backfill to 1 the next commands emit 2, 3, 4")]
    public async Task BackfilledRow_ContinuesAt2()
    {
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name, aggregate_version) VALUES ('{widgetId}', 'migrated', 1)");

        for (var expectedLoaded = 1L; expectedLoaded <= 3; expectedLoaded++)
        {
            await RenameInOwnUnitOfWork(widgetId, $"n{expectedLoaded}", expectedLoaded);
        }

        (await OutboxVersions(widgetId)).ShouldBe([2L, 3L, 4L]);
        (await StoredVersion(widgetId)).ShouldBe(4);
    }

    [Fact(DisplayName = "ADR-0019: several events in one command take consecutive versions and the row keeps the last")]
    public async Task ManyEventsInOneCommand_PersistTheLastVersion()
    {
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name, aggregate_version) VALUES ('{widgetId}', 'x', 7)");

        await using (var uow = NewUnitOfWork())
        {
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = await ctx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);
            widget.Rename("a", 1m, RoundedSystemTimeProvider.Instance.GetUtcNow());
            widget.Touch();
            widget.Rename("b", 1m, RoundedSystemTimeProvider.Instance.GetUtcNow());
            await uow.CommitAsync(Ct);
        }

        (await OutboxVersions(widgetId)).ShouldBe([8L, 9L, 10L]);
        (await StoredVersion(widgetId)).ShouldBe(10);
    }

    [Fact(DisplayName = "ADR-0019: an event-only change (no other column changes) persists the version bump")]
    public async Task EventOnlyChange_PersistsTheVersionBump()
    {
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name, aggregate_version) VALUES ('{widgetId}', 'same', 1)");

        for (var i = 0; i < 2; i++)
        {
            await using var uow = NewUnitOfWork();
            await uow.BeginAsync(Ct);
            await using var ctx = new WidgetDbContext(uow);
            var widget = await ctx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);
            widget.Touch();
            await uow.CommitAsync(Ct);
        }

        (await OutboxVersions(widgetId)).ShouldBe([2L, 3L]);
        (await StoredVersion(widgetId)).ShouldBe(3);
        (await db.ScalarAsync<string>("SELECT name FROM uow_test.widget WHERE id = $1", widgetId)).ShouldBe("same");
    }

    [Theory(DisplayName = "ADR-0019: two concurrent commands on one aggregate: exactly one commits, the other conflicts, no duplicate version")]
    [InlineData(false)] // both rename (other columns change too)
    [InlineData(true)] // both only raise an event (aggregate_version is the only changed column)
    public async Task ConcurrentCommands_OneCommits_OtherConflicts_NoDuplicateVersion(bool eventOnly)
    {
        const long loadedVersion = 4;
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name, aggregate_version) VALUES ('{widgetId}', 'base', {loadedVersion})");

        // Rendezvous: both commands must have loaded version N before either one commits.
        TaskCompletionSource[] loaded = [NewSignal(), NewSignal()];
        var backendPids = new int[2];

        async Task<Exception?> Command(int index)
        {
            try
            {
                await using var uow = NewUnitOfWork();
                await uow.BeginAsync(Ct);
                await using (var pid = new NpgsqlCommand("SELECT pg_backend_pid()", uow.Connection, uow.Transaction))
                {
                    backendPids[index] = (int)(await pid.ExecuteScalarAsync(Ct))!;
                }

                await using var ctx = new WidgetDbContext(uow);
                var widget = await ctx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);
                widget.AggregateVersion.ShouldBe(loadedVersion);

                loaded[index].SetResult();
                await loaded[1 - index].Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

                if (eventOnly)
                {
                    widget.Touch();
                }
                else
                {
                    widget.Rename($"by-{index}", 1m, RoundedSystemTimeProvider.Instance.GetUtcNow());
                }

                await uow.CommitAsync(Ct);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => Command(0), Ct), Task.Run(() => Command(1), Ct));

        backendPids[0].ShouldNotBe(backendPids[1]); // really two connections/transactions
        results.Count(r => r is null).ShouldBe(1);
        results.Single(r => r is not null).ShouldBeOfType<DbUpdateConcurrencyException>();

        (await OutboxVersions(widgetId)).ShouldBe([loadedVersion + 1]);
        (await StoredVersion(widgetId)).ShouldBe(loadedVersion + 1);
    }

    [Fact(DisplayName = "ADR-0019 / BE-R3: a stale xmin still conflicts when aggregate_version did not change")]
    public async Task StaleXmin_WithUnchangedAggregateVersion_StillConflicts()
    {
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name, aggregate_version) VALUES ('{widgetId}', 'v0', 2)");

        await using var uow = NewUnitOfWork();
        await uow.BeginAsync(Ct);
        await using var ctx = new WidgetDbContext(uow);
        var widget = await ctx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);

        // A write that leaves aggregate_version alone but changes xmin (the ETag), committed in between.
        await db.ExecuteAsync($"UPDATE uow_test.widget SET name = 'sql' WHERE id = '{widgetId}'");

        widget.Rename("stale", 1m, RoundedSystemTimeProvider.Instance.GetUtcNow());
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => uow.CommitAsync(Ct));

        (await OutboxVersions(widgetId)).ShouldBeEmpty();
        (await StoredVersion(widgetId)).ShouldBe(2);
        (await db.ScalarAsync<string>("SELECT name FROM uow_test.widget WHERE id = $1", widgetId)).ShouldBe("sql");
    }

    [Fact(DisplayName = "ADR-0019: a stale aggregate_version conflicts even when xmin is not compared")]
    public async Task StaleAggregateVersion_Conflicts()
    {
        var widgetId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO uow_test.widget (id, name, aggregate_version) VALUES ('{widgetId}', 'v0', 2)");

        await using var uow = NewUnitOfWork();
        await uow.BeginAsync(Ct);
        await using var ctx = new WidgetDbContext(uow);
        var widget = await ctx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);

        // Pretend xmin is current (as if re-read), so only aggregate_version can catch the race.
        await db.ExecuteAsync($"UPDATE uow_test.widget SET aggregate_version = 3 WHERE id = '{widgetId}'");
        var entry = ctx.Entry(widget);
        entry.Property(RowVersion.PropertyName).OriginalValue =
            await db.ScalarAsync<uint>("SELECT xmin FROM uow_test.widget WHERE id = $1", widgetId);

        widget.Touch();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => uow.CommitAsync(Ct));

        (await OutboxVersions(widgetId)).ShouldBeEmpty();
        (await StoredVersion(widgetId)).ShouldBe(3);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private UnitOfWork NewUnitOfWork() => db.CreateUnitOfWork(_dataSource, _userId, Guid.CreateVersion7());

    private async Task RenameInOwnUnitOfWork(Guid widgetId, string newName, long expectedLoadedVersion)
    {
        await using var uow = NewUnitOfWork();
        await uow.BeginAsync(Ct);
        await using var ctx = new WidgetDbContext(uow);
        var widget = await ctx.Widgets.SingleAsync(w => w.Id == widgetId, Ct);
        widget.AggregateVersion.ShouldBe(expectedLoadedVersion);
        widget.Rename(newName, 1m, RoundedSystemTimeProvider.Instance.GetUtcNow());
        await uow.CommitAsync(Ct);
    }

    private async Task<long> StoredVersion(Guid widgetId) =>
        await db.ScalarAsync<long>("SELECT aggregate_version FROM uow_test.widget WHERE id = $1", widgetId);

    private async Task<List<long>> OutboxVersions(Guid widgetId)
    {
        await using var cmd = db.Admin.CreateCommand(
            "SELECT aggregate_version FROM outbox.message WHERE aggregate_id = $1 ORDER BY aggregate_version");
        cmd.Parameters.Add(new NpgsqlParameter { Value = widgetId });
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        var versions = new List<long>();
        while (await reader.ReadAsync(Ct))
        {
            versions.Add(reader.GetInt64(0));
        }

        return versions;
    }
}
