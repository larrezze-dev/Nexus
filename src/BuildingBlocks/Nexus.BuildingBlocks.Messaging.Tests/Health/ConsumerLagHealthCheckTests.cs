using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexus.BuildingBlocks.Messaging.Health;
using Shouldly;

namespace Nexus.BuildingBlocks.Messaging.Tests.Health;

/// <summary>ConsumerHost health (step 0.7 track S: /health reports lag per group).</summary>
public sealed class ConsumerLagHealthCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeProbe : IConsumerLagProbe
    {
        public Dictionary<string, long> Lags { get; } = new(StringComparer.Ordinal);

        public bool Throw { get; set; }

        /// <summary>
        /// Blocks the CALLING thread like a Kafka admin call stuck on an unreachable broker, but
        /// cooperatively: it is released as soon as the probe's token is cancelled (or after BlockFor), so no
        /// thread-pool thread stays pinned after the test. It records the maximum number of concurrently
        /// blocked calls (a caller that does not offload the call to the thread pool serializes them: max 1),
        /// whether the token it received can be cancelled at all, and whether it was cancelled when released
        /// (a caller that does not pass the budget token leaves the call blocked for the full BlockFor).
        /// </summary>
        public TimeSpan? BlockFor { get; set; }

        private int _calls;
        private int _inFlight;
        private int _maxInFlight;
        private int _released;
        private int _tokensNotCancelable;
        private int _releasedWithoutCancellation;

        public int MaxConcurrency => Volatile.Read(ref _maxInFlight);

        public int Released => Volatile.Read(ref _released);

        public int TokensNotCancelable => Volatile.Read(ref _tokensNotCancelable);

        public int ReleasedWithoutCancellation => Volatile.Read(ref _releasedWithoutCancellation);

        public int Calls => Volatile.Read(ref _calls);

        public Task<ConsumerGroupLag> GetLagAsync(string groupId, string topic, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (BlockFor is { } block)
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

                ct.WaitHandle.WaitOne(block);
                Interlocked.Decrement(ref _inFlight);
                if (!ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _releasedWithoutCancellation);
                }

                Interlocked.Increment(ref _released);
                ct.ThrowIfCancellationRequested(); // like the real probe once its budget is spent
            }

            if (Throw)
            {
                throw new InvalidOperationException("broker down");
            }

            return Task.FromResult(new ConsumerGroupLag(groupId, topic, Lags.GetValueOrDefault(groupId)));
        }
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
        var (check, registry, probe, _) = Create(queryTimeout: TimeSpan.FromSeconds(1));
        using var _ = check;
        foreach (var g in new[] { "operations.a", "operations.b", "operations.c", "operations.d" })
        {
            registry.Register(g, $"t.{g}");
            registry.Report(g, ConsumerLoopState.Running);
        }

        // 4 groups x 5 s if probed serially = 20 s; one 1 s budget for the whole round = ~1 s (x4 margin below).

        probe.BlockFor = TimeSpan.FromSeconds(5);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await check.CheckHealthAsync(Context(), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        started.Stop();

        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4), "one budget for the whole round, not N x the blocking call");
        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["operations.a.lag"].ShouldBe("unknown");
        result.Data["operations.d.lag"].ShouldBe("unknown");

        // Offloaded to the thread pool: all 4 blocking calls ran at the same time.
        probe.MaxConcurrency.ShouldBe(4, "each probe must be offloaded (Task.Run) so blocking calls run in parallel");
        probe.TokensNotCancelable.ShouldBe(0, "the budget token must be passed to every probe");

        // The budget token is cancelled when the budget elapses: every blocked probe is released by it,
        // well before its own 5 s block would end.
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (probe.Released < 4 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Ct);
        }

        probe.Released.ShouldBe(4, "the budget token must be cancelled once the budget elapses");
        probe.ReleasedWithoutCancellation.ShouldBe(0);
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
