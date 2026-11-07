using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexus.BuildingBlocks.Messaging.Health;
using Shouldly;

namespace Nexus.BuildingBlocks.Messaging.Tests.Health;

/// <summary>ConsumerHost health (step 0.7 track S: /health reports lag per group).</summary>
public sealed class ConsumerLagHealthCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Wall clock and timers under the test's control: <see cref="Now"/> moves only when the test sets it or calls
    /// <see cref="Advance"/>, and timers created through this provider (the round's budget) fire only from
    /// <see cref="Advance"/>, on the calling thread. One-shot timers only (all the health check uses).
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly Lock _lock = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        private long _sequence;

        public DateTimeOffset Now
        {
            get { lock (_lock) { return _now; } }
            set { lock (_lock) { _now = value; } }
        }

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
            {
                throw new NotSupportedException("ManualClock only supports one-shot timers.");
            }

            ManualTimer timer;
            lock (_lock)
            {
                timer = new ManualTimer(this, callback, state, ++_sequence);
                _timers.Add(timer);
            }

            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>
        /// Moves time forward and fires every timer that falls due. Timers due at the same instant fire
        /// LATEST-CREATED FIRST: the system timer queue gives no ordering guarantee between them, so the test
        /// takes the adversarial order (a timer created after the budget's must not be able to pre-empt it).
        /// </summary>
        public void Advance(TimeSpan delta)
        {
            List<ManualTimer> due;
            DateTimeOffset now;
            lock (_lock)
            {
                now = _now += delta;
                due = _timers.Where(t => t.Due <= now).OrderBy(t => t.Due).ThenByDescending(t => t.Sequence).ToList();
            }

            foreach (var timer in due)
            {
                timer.FireIfDue(now);
            }
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, long sequence) : ITimer
        {
            public long Sequence { get; } = sequence;

            public DateTimeOffset? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._lock)
                {
                    if (!clock._timers.Contains(this))
                    {
                        return false; // disposed
                    }

                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                    return true;
                }
            }

            public void FireIfDue(DateTimeOffset now)
            {
                lock (clock._lock)
                {
                    // Disposed (or re-armed) by an earlier callback of the same Advance: it must not fire.
                    if (!clock._timers.Contains(this) || Due is not { } due || due > now)
                    {
                        return;
                    }

                    Due = null;
                }

                callback(state);
            }

            public void Dispose()
            {
                lock (clock._lock)
                {
                    clock._timers.Remove(this);
                    Due = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeProbe : IConsumerLagProbe, IDisposable
    {
        private readonly ManualResetEventSlim _unblock = new(false);
        private int _calls;
        private int _inFlight;
        private int _maxInFlight;
        private int _released;
        private int _tokensNotCancelable;
        private int _releasedWithoutCancellation;

        public Dictionary<string, long> Lags { get; } = new(StringComparer.Ordinal);

        public bool Throw { get; set; }

        /// <summary>
        /// Every call blocks the CALLING thread like a Kafka admin call stuck on an unreachable broker, with no
        /// timeout of its own: only the cancellation of the token it received releases it (or <see cref="Dispose"/>,
        /// the test's cleanup, so no pool thread stays pinned after a failure). It records the maximum number of
        /// concurrently blocked calls (a caller that does not offload them to the thread pool serializes them:
        /// max 1), whether the token it received can be cancelled at all, and how each call was released.
        /// </summary>
        public bool Block { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public int InFlight => Volatile.Read(ref _inFlight);

        public int MaxConcurrency => Volatile.Read(ref _maxInFlight);

        public int Released => Volatile.Read(ref _released);

        public int TokensNotCancelable => Volatile.Read(ref _tokensNotCancelable);

        public int ReleasedWithoutCancellation => Volatile.Read(ref _releasedWithoutCancellation);

        public Task<ConsumerGroupLag> GetLagAsync(string groupId, string topic, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (Block)
            {
                BlockUntilCancelled(ct);
            }

            if (Throw)
            {
                throw new InvalidOperationException("broker down");
            }

            return Task.FromResult(new ConsumerGroupLag(groupId, topic, Lags.GetValueOrDefault(groupId)));
        }

        private void BlockUntilCancelled(CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
            {
                Interlocked.Increment(ref _tokensNotCancelable);
            }

            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
            {
            }

            var cancelled = false;
            try
            {
                // Registers on the token (not ct.WaitHandle, which throws once the source is disposed): released
                // when the token is cancelled, even if its source was disposed after the cancellation.
                _unblock.Wait(ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
                if (!cancelled)
                {
                    Interlocked.Increment(ref _releasedWithoutCancellation);
                }

                Interlocked.Increment(ref _released);
            }

            ct.ThrowIfCancellationRequested(); // like the real probe once its budget is spent
        }

        public void Dispose()
        {
            _unblock.Set(); // never disposed: a late call may still be about to wait on it
        }
    }

    /// <summary>Polls <paramref name="condition"/> in real time; the limit only bounds a FAILING test.</summary>
    private static async Task<bool> EventuallyAsync(Func<bool> condition, TimeSpan limit)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > limit)
            {
                return false;
            }

            await Task.Delay(10, Ct);
        }

        return true;
    }

    private static (ConsumerLagHealthCheck Check, ConsumerHealthRegistry Registry, FakeProbe Probe, ManualClock Clock) Create(
        long threshold = 100, TimeSpan? cache = null, TimeSpan? queryTimeout = null)
    {
        var clock = new ManualClock();
        var registry = new ConsumerHealthRegistry(clock);
        var probe = new FakeProbe();
        var options = Microsoft.Extensions.Options.Options.Create(new ConsumerHealthOptions
        {
            LagDegradedThreshold = threshold,
            CacheDuration = cache ?? TimeSpan.FromSeconds(5),
            QueryTimeout = queryTimeout ?? TimeSpan.FromSeconds(2),
        });
        return (new ConsumerLagHealthCheck(registry, probe, options, clock), registry, probe, clock);
    }

    private static HealthCheckContext Context() => new();

    [Fact(DisplayName = "ConsumerHost health: running consumers under the lag threshold are Healthy and the lag per group is reported")]
    public async Task Running_UnderThreshold_Healthy()
    {
        var (check, registry, probe, _) = Create();
        using var _ = check;
        registry.Register("operations.a", "t.a");
        registry.Report("operations.a", ConsumerLoopState.Running);
        probe.Lags["operations.a"] = 100;

        var result = await check.CheckHealthAsync(Context(), Ct);

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["operations.a.lag"].ShouldBe(100L);
        result.Data["operations.a.state"].ShouldBe("Running");
    }

    [Fact(DisplayName = "ConsumerHost health: a group with lag above the threshold makes the check Degraded")]
    public async Task LagAboveThreshold_Degraded()
    {
        var (check, registry, probe, _) = Create(threshold: 100);
        using var _ = check;
        registry.Register("operations.a", "t.a");
        registry.Register("operations.b", "t.b");
        probe.Lags["operations.b"] = 101;

        var result = await check.CheckHealthAsync(Context(), Ct);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description!.ShouldContain("operations.b");
        result.Description!.ShouldNotContain("operations.a");
    }

    [Fact(DisplayName = "ConsumerHost health: a dead consumer loop makes the check Unhealthy, without leaking the exception text")]
    public async Task FaultedLoop_Unhealthy()
    {
        var (check, registry, _, _) = Create();
        using var _ = check;
        registry.Register("operations.a", "t.a");
        registry.Report("operations.a", ConsumerLoopState.Faulted, new InvalidOperationException("password=secret"));

        var result = await check.CheckHealthAsync(Context(), Ct);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Data["operations.a.state"].ShouldBe("Faulted");
        result.Data.Values.OfType<string>().ShouldNotContain(v => v.Contains("secret", StringComparison.Ordinal));
        result.Description!.ShouldNotContain("secret");
    }

    [Fact(DisplayName = "ConsumerHost health: when the lag cannot be read the check is Degraded, not Unhealthy")]
    public async Task ProbeFails_Degraded()
    {
        var (check, registry, probe, _) = Create();
        using var _ = check;
        registry.Register("operations.a", "t.a");
        probe.Throw = true;

        var result = await check.CheckHealthAsync(Context(), Ct);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["operations.a.lag"].ShouldBe("unknown");
    }

    [Fact(DisplayName = "ConsumerHost health: a probe round is bounded by one budget (QueryTimeout) whatever the number of groups; late groups are Degraded/unknown")]
    public async Task BlockingProbes_AreBoundedByOneBudget()
    {
        // The budget runs on the test's clock, not on wall time: nothing expires while the (possibly busy, low-core)
        // thread pool starts the probes, and the budget elapses exactly when the test says so.
        var (check, registry, probe, clock) = Create(queryTimeout: TimeSpan.FromSeconds(1));
        using var _ = check;
        using var __ = probe; // only releases calls still blocked when an assertion below fails
        string[] groups = ["operations.a", "operations.b", "operations.c", "operations.d"];
        foreach (var g in groups)
        {
            registry.Register(g, $"t.{g}");
            registry.Report(g, ConsumerLoopState.Running);
        }

        probe.Block = true;

        // Off the test thread: a caller that ran the probes inline would otherwise block the test itself.
        var round = Task.Run(() => check.CheckHealthAsync(Context(), Ct), Ct);

        // Every probe is offloaded to the thread pool, so all the blocking calls are in flight at once. A caller
        // that does not offload them serializes them (concurrency 1). The limit only bounds a failing run.
        (await EventuallyAsync(() => probe.InFlight == groups.Length, TimeSpan.FromSeconds(20))).ShouldBeTrue(
            $"each probe must be offloaded (Task.Run) so blocking calls run in parallel; in flight: {probe.InFlight}");
        probe.MaxConcurrency.ShouldBe(groups.Length);
        probe.TokensNotCancelable.ShouldBe(0, "the budget token must be passed to every probe");
        round.IsCompleted.ShouldBeFalse("the round waits for the probes while the budget lasts");

        // ONE budget for the whole round, not one per group: once it elapses the round is over...
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await round.WaitAsync(TimeSpan.FromSeconds(20), Ct);

        result.Status.ShouldBe(HealthStatus.Degraded);
        foreach (var g in groups)
        {
            result.Data[$"{g}.lag"].ShouldBe("unknown");
        }

        // ...and the token every probe holds has been cancelled: each blocked call is released by it (the fake has
        // no timeout of its own), none is left running on the pool after the round has returned.
        (await EventuallyAsync(() => probe.Released == groups.Length, TimeSpan.FromSeconds(20))).ShouldBeTrue(
            $"the budget token must be cancelled when the round ends; released: {probe.Released} of {groups.Length}");
        probe.ReleasedWithoutCancellation.ShouldBe(0);
        probe.Calls.ShouldBe(groups.Length);
    }

    [Fact(DisplayName = "ConsumerHost health: a loop paused because its DLQ cannot be written is Unhealthy")]
    public async Task DlqUnavailable_Unhealthy()
    {
        var (check, registry, _, _) = Create();
        using var _ = check;
        registry.Register("operations.a", "t.a");
        registry.Report("operations.a", ConsumerLoopState.DlqUnavailable, new InvalidOperationException("broker down"));

        var result = await check.CheckHealthAsync(Context(), Ct);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Data["operations.a.state"].ShouldBe("DlqUnavailable");
    }

    [Fact(DisplayName = "ConsumerHost health: a loop with transient poll errors is Degraded")]
    public async Task TransientPollErrors_Degraded()
    {
        var (check, registry, _, _) = Create();
        using var _ = check;
        registry.Register("operations.a", "t.a");
        registry.Report("operations.a", ConsumerLoopState.Degraded);

        var result = await check.CheckHealthAsync(Context(), Ct);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description!.ShouldContain("poll errors: operations.a");
    }

    [Fact(DisplayName = "ConsumerHost health: the result is cached for at least 5 s even if a shorter cache is configured")]
    public async Task ResultIsCachedAtLeastFiveSeconds()
    {
        var (check, registry, probe, clock) = Create(cache: TimeSpan.FromSeconds(1));
        using var _ = check;
        registry.Register("operations.a", "t.a");

        await check.CheckHealthAsync(Context(), Ct);
        clock.Now += TimeSpan.FromSeconds(4.9);
        await check.CheckHealthAsync(Context(), Ct);
        probe.Calls.ShouldBe(1);

        clock.Now += TimeSpan.FromSeconds(0.2);
        await check.CheckHealthAsync(Context(), Ct);
        probe.Calls.ShouldBe(2);
    }

    [Fact(DisplayName = "ConsumerHost health: two live consumers of one host cannot share a consumer group")]
    public void DuplicateGroup_Rejected()
    {
        var registry = new ConsumerHealthRegistry(TimeProvider.System);
        registry.Register("operations.a", "t.a");
        Should.Throw<InvalidOperationException>(() => registry.Register("operations.a", "t.b"));

        registry.Report("operations.a", ConsumerLoopState.Stopped);
        registry.Register("operations.a", "t.a"); // a stopped instance may be replaced
    }
}
