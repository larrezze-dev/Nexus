using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Npgsql;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>B5: daily purge (conventions-events.md §4.6) and relay metrics, against the real table as svc_relay.</summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class OutboxPurgeAndMetricsTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "EV-PURGE: §4.6 deletes only PUBLISHED rows older than 7 days; keeps PUBLISHED at 6 days, PENDING and FAILED forever")]
    public async Task Purge_DeletesOnlyOldPublished()
    {
        await OutboxRows.ClearAsync(db);
        var old = await InsertPublishedAsync(Now.AddDays(-8));
        var exactlyOld = await InsertPublishedAsync(Now.AddDays(-7).AddSeconds(-1));
        var recent = await InsertPublishedAsync(Now.AddDays(-6));
        var pending = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, Now.AddDays(-100));
        var failed = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, Now.AddDays(-100), status: "FAILED", attempts: 10);

        var (service, _) = CreateMaintenance(new FakeTime(Now));
        await using var lease = await OutboxRows.AcquireAsync(db);
        (await service.PurgeIfDueAsync(lease, CancellationToken.None)).ShouldBe(2);

        (await Exists(old)).ShouldBeFalse();
        (await Exists(exactlyOld)).ShouldBeFalse();
        (await Exists(recent)).ShouldBeTrue();
        (await Exists(pending)).ShouldBeTrue();
        (await Exists(failed)).ShouldBeTrue();
    }

    [Fact(DisplayName = "EV-PURGE: §4.6 purge works in chunks (12 rows, chunk 5) and runs at most once per 24 h")]
    public async Task Purge_Chunks_AndRunsOncePerInterval()
    {
        await OutboxRows.ClearAsync(db);
        for (var i = 0; i < 12; i++)
        {
            await InsertPublishedAsync(Now.AddDays(-10));
        }

        var time = new FakeTime(Now);
        var (service, options) = CreateMaintenance(time, chunk: 5);
        using var meters = new TestMeterFactory();
        await using var lease = await OutboxRows.AcquireAsync(db);

        (await service.PurgeIfDueAsync(lease, CancellationToken.None)).ShouldBe(12);
        (await db.ScalarAsync<long>("SELECT count(*) FROM outbox.message")).ShouldBe(0);

        // Not due again within 24 h, even with new old rows.
        await InsertPublishedAsync(Now.AddDays(-10));
        time.Advance(TimeSpan.FromHours(23));
        (await service.PurgeIfDueAsync(lease, CancellationToken.None)).ShouldBe(0);
        time.Advance(TimeSpan.FromHours(2));
        (await service.PurgeIfDueAsync(lease, CancellationToken.None)).ShouldBe(1);
        options.PurgeIntervalHours.ShouldBe(24);
    }

    [Fact(DisplayName = "EV-PURGE: §4.6 the leader's loop purges old PUBLISHED rows")]
    public async Task LeaderLoop_Purges()
    {
        await OutboxRows.ClearAsync(db);
        var old = await InsertPublishedAsync(DateTimeOffset.UtcNow.AddDays(-8));
        using var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(async () => !await Exists(old));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact(DisplayName = "EV-PURGE: §4.1/§4.6 a non-leader instance never purges")]
    public async Task NonLeader_NeverPurges()
    {
        await OutboxRows.ClearAsync(db);
        var old = await InsertPublishedAsync(DateTimeOffset.UtcNow.AddDays(-8));
        await using var leader = await OutboxRows.AcquireAsync(db); // someone else holds the lock
        using var worker = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); // several LeaderRetrySeconds (1 s) attempts
            (await Exists(old)).ShouldBeTrue();
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact(DisplayName = "EV-PURGE: nexus_outbox_purged_total counts deleted rows")]
    public async Task Purge_CountsMetric()
    {
        await OutboxRows.ClearAsync(db);
        await InsertPublishedAsync(Now.AddDays(-9));
        await InsertPublishedAsync(Now.AddDays(-9));
        using var meters = new TestMeterFactory();
        using var listener = new MetricListener(meters);
        var service = new OutboxMaintenance(new RelayMetrics(meters), new RelayOptions(), new FakeTime(Now), NullLogger.Instance);
        await using var lease = await OutboxRows.AcquireAsync(db);

        await service.PurgeIfDueAsync(lease, CancellationToken.None);

        listener.Sum(RelayMetrics.PurgedTotalName).ShouldBe(2);
    }

    [Fact(DisplayName = "Metrics: publish latency (published_at - created_at), batch size and errors by kind are recorded")]
    public async Task Metrics_LatencyBatchSizeAndErrors()
    {
        using var meters = new TestMeterFactory();
        using var listener = new MetricListener(meters);
        var time = new FakeTime(Now);
        var ok = FakeBatch.Message(Guid.CreateVersion7(), 1) with { CreatedAt = Now.AddSeconds(-3) };
        var rowFail = FakeBatch.Message(Guid.CreateVersion7(), 1);
        var transient = FakeBatch.Message(Guid.CreateVersion7(), 1);
        var publisher = new ScriptedPublisher(m =>
            m.Id == ok.Id ? PublishResult.Success
            : m.Id == rowFail.Id ? PublishResult.Failure("bad row", false)
            : PublishResult.Failure("kafka down", true));

        await new OutboxBatchProcessor(publisher, new RelayMetrics(meters), time, 10, NullLogger.Instance)
            .ProcessAsync(new FakeBatch(ok, rowFail, transient), CancellationToken.None);

        listener.Values(RelayMetrics.PublishLatencyName).ShouldBe([3.0]);
        listener.Values(RelayMetrics.BatchSizeName).ShouldBe([3.0]);
        listener.Tagged(RelayMetrics.ErrorsTotalName, "kind", RelayMetrics.ErrorKindRow).ShouldBe(1);
        listener.Tagged(RelayMetrics.ErrorsTotalName, "kind", RelayMetrics.ErrorKindTemporary).ShouldBe(1);
    }

    [Fact(DisplayName = "Metrics: nexus_outbox_pending gauge reflects the PENDING count, refreshed at most every 5 s")]
    public async Task PendingGauge_ReflectsCount()
    {
        await OutboxRows.ClearAsync(db);
        await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, Now);
        await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, Now);
        await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, Now, status: "FAILED", attempts: 10);
        using var meters = new TestMeterFactory();
        using var listener = new MetricListener(meters);
        var time = new FakeTime(Now);
        var service = new OutboxMaintenance(new RelayMetrics(meters), new RelayOptions(), time, NullLogger.Instance);
        await using var lease = await OutboxRows.AcquireAsync(db);

        await service.RefreshPendingAsync(lease, CancellationToken.None);
        listener.Observe(RelayMetrics.PendingName).ShouldBe(2);

        await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, Now);
        await service.RefreshPendingAsync(lease, CancellationToken.None); // too soon: cached
        listener.Observe(RelayMetrics.PendingName).ShouldBe(2);

        time.Advance(TimeSpan.FromSeconds(6));
        await service.RefreshPendingAsync(lease, CancellationToken.None);
        listener.Observe(RelayMetrics.PendingName).ShouldBe(3);
    }

    private static (OutboxMaintenance Service, RelayOptions Options) CreateMaintenance(TimeProvider time, int chunk = 5000)
    {
        var options = new RelayOptions { PurgeChunkSize = chunk };
        return (new OutboxMaintenance(new RelayMetrics(new TestMeterFactory()), options, time, NullLogger.Instance), options);
    }

    private RelayWorker CreateWorker() =>
        new(
            OutboxRows.Leadership(db),
            new ScriptedPublisher(_ => PublishResult.Success),
            new RelayMetrics(new TestMeterFactory()),
            Options.Create(new RelayOptions { PollIntervalMs = 100, LeaderRetrySeconds = 1 }),
            TimeProvider.System,
            NullLogger<RelayWorker>.Instance);

    private async Task<Guid> InsertPublishedAsync(DateTimeOffset publishedAt)
    {
        var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, publishedAt.AddMinutes(-1), status: "PUBLISHED");
        await using var command = db.Admin.CreateCommand("UPDATE outbox.message SET published_at = $1 WHERE id = $2");
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = publishedAt });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<bool> Exists(Guid id)
    {
        await using var command = db.Admin.CreateCommand("SELECT EXISTS (SELECT 1 FROM outbox.message WHERE id = $1)");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(50);
        }
    }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Collects measurements of one factory's instruments; gauges are read on demand.</summary>
    private sealed class MetricListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

        public MetricListener(IMeterFactory factory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == factory)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i.Name, v, t));
            _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Add(i.Name, v, t));
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i.Name, v, t));
            _listener.Start();
        }

        public double Sum(string name) => Values(name).Sum();

        public List<double> Values(string name)
        {
            lock (_measurements)
            {
                return _measurements.Where(m => m.Name == name).Select(m => m.Value).ToList();
            }
        }

        public double Tagged(string name, string key, string value)
        {
            lock (_measurements)
            {
                return _measurements.Where(m => m.Name == name && m.Tags.Any(t => t.Key == key && (string?)t.Value == value)).Sum(m => m.Value);
            }
        }

        /// <summary>Polls the observable instruments and returns the last value of <paramref name="name"/>.</summary>
        public double Observe(string name)
        {
            lock (_measurements)
            {
                _measurements.RemoveAll(m => m.Name == name);
            }

            _listener.RecordObservableInstruments();
            return Values(name).Last();
        }

        public void Dispose() => _listener.Dispose();

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (_measurements)
            {
                _measurements.Add((name, value, tags.ToArray()));
            }
        }
    }
}
