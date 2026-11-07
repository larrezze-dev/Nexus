using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Nexus.BuildingBlocks.Messaging.Health;

/// <summary>Thresholds of <see cref="ConsumerLagHealthCheck"/>, bound from <c>Nexus:Consumers:Health</c>.</summary>
public sealed class ConsumerHealthOptions
{
    public const string SectionName = "Nexus:Consumers:Health";

    /// <summary>A group whose lag exceeds this many records makes the check <c>Degraded</c>.</summary>
    public long LagDegradedThreshold { get; set; } = 1000;

    /// <summary>How long a result is reused before Kafka is queried again (minimum 5 s).</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Total time budget of one probe round (every group, in parallel). A group whose lag is not read
    /// within it is reported as <c>unknown</c> (Degraded). Each admin call of the probe gets at most what
    /// is left of this budget, so no thread stays blocked on Kafka much longer than it.
    /// </summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Health of the host's consumers (step 0.7: <c>/health</c> reports lag per group):
/// <list type="bullet">
///   <item><c>Unhealthy</c> if any consumer loop died (<see cref="ConsumerLoopState.Faulted"/>) or is paused
///   because its DLQ cannot be written (<see cref="ConsumerLoopState.DlqUnavailable"/>);</item>
///   <item><c>Degraded</c> if any loop reports transient poll errors (<see cref="ConsumerLoopState.Degraded"/>),
///   any group's lag is above <see cref="ConsumerHealthOptions.LagDegradedThreshold"/>, or the lag could not
///   be read within <see cref="ConsumerHealthOptions.QueryTimeout"/> (Kafka unreachable: the consumers
///   themselves keep retrying);</item>
///   <item><c>Healthy</c> otherwise.</item>
/// </list>
/// The data dictionary carries, per group, <c>&lt;group&gt;.state</c> and <c>&lt;group&gt;.lag</c>. The result
/// is cached for <see cref="ConsumerHealthOptions.CacheDuration"/> (≥ 5 s) so frequent probes do not
/// hammer the cluster.
/// </summary>
public sealed class ConsumerLagHealthCheck : IHealthCheck, IDisposable
{
    private static readonly TimeSpan MinimumCache = TimeSpan.FromSeconds(5);

    private readonly ConsumerHealthRegistry _registry;
    private readonly IConsumerLagProbe _probe;
    private readonly ConsumerHealthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HealthCheckResult? _cached;
    private DateTimeOffset _cachedAt;

    public ConsumerLagHealthCheck(
        ConsumerHealthRegistry registry,
        IConsumerLagProbe probe,
        IOptions<ConsumerHealthOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _registry = registry;
        _probe = probe;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    private TimeSpan CacheDuration => _options.CacheDuration < MinimumCache ? MinimumCache : _options.CacheDuration;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_cached is { } cached && now - _cachedAt < CacheDuration)
            {
                return cached;
            }

            var result = await EvaluateAsync(cancellationToken).ConfigureAwait(false);
            _cached = result;
            _cachedAt = now;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HealthCheckResult> EvaluateAsync(CancellationToken ct)
    {
        var consumers = _registry.Snapshot();
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        var unhealthy = new List<string>();
        var degraded = new List<string>();
        var lagging = new List<string>();
        var unknown = new List<string>();

        var lags = await ProbeAllAsync(consumers, ct).ConfigureAwait(false);

        foreach (var consumer in consumers)
        {
            data[$"{consumer.GroupId}.state"] = consumer.State.ToString();
            switch (consumer.State)
            {
                case ConsumerLoopState.Faulted or ConsumerLoopState.DlqUnavailable:
                    // The exception itself goes to the logs only: /health is unauthenticated.
                    unhealthy.Add(consumer.GroupId);
                    break;
                case ConsumerLoopState.Degraded:
                    degraded.Add(consumer.GroupId);
                    break;
            }

            if (lags.TryGetValue(consumer.GroupId, out var lag))
            {
                data[$"{consumer.GroupId}.lag"] = lag;
                if (lag > _options.LagDegradedThreshold)
                {
                    lagging.Add(consumer.GroupId);
                }
            }
            else
            {
                data[$"{consumer.GroupId}.lag"] = "unknown";
                unknown.Add(consumer.GroupId);
            }
        }

        if (unhealthy.Count > 0)
        {
            return HealthCheckResult.Unhealthy($"Consumer loop not consuming: {string.Join(", ", unhealthy)}", data: data);
        }

        if (degraded.Count > 0 || lagging.Count > 0 || unknown.Count > 0)
        {
            var parts = new List<string>();
            if (degraded.Count > 0)
            {
                parts.Add($"poll errors: {string.Join(", ", degraded)}");
            }

            if (lagging.Count > 0)
            {
                parts.Add($"lag above {_options.LagDegradedThreshold}: {string.Join(", ", lagging)}");
            }

            if (unknown.Count > 0)
            {
                parts.Add($"lag unknown: {string.Join(", ", unknown)}");
            }

            return HealthCheckResult.Degraded(string.Join("; ", parts), data: data);
        }

        return HealthCheckResult.Healthy($"{consumers.Count} consumer(s) running", data);
    }

    /// <summary>
    /// Probes every group in parallel within ONE budget (<see cref="ConsumerHealthOptions.QueryTimeout"/>).
    /// Each probe runs on the thread pool (the Kafka admin calls block) and gets a token cancelled at the
    /// end of the budget; a group whose probe failed or did not finish in time is simply absent from the
    /// result. The round never waits longer than the budget, whatever the number of groups.
    /// </summary>
    private async Task<Dictionary<string, long>> ProbeAllAsync(IReadOnlyList<ConsumerStatus> consumers, CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (consumers.Count == 0)
        {
            return result;
        }

        var budget = _options.QueryTimeout > TimeSpan.Zero ? _options.QueryTimeout : TimeSpan.FromSeconds(2);
        using var timeout = new CancellationTokenSource(budget, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var token = linked.Token;

        var probes = consumers
            .Select(c => (c.GroupId, Task: Task.Run(() => _probe.GetLagAsync(c.GroupId, c.Topic, token), token)))
            .ToList();

        try
        {
            await Task.WhenAll(probes.Select(p => p.Task)).WaitAsync(budget, _timeProvider, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Timeout or a failed probe: the groups without a result are reported as unknown below.
        }

        foreach (var (groupId, task) in probes)
        {
            if (task.IsCompletedSuccessfully)
            {
                result[groupId] = task.Result.Lag;
            }
            else
            {
                // Observe late failures so they never surface as unobserved task exceptions.
                _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        return result;
    }

    public void Dispose() => _gate.Dispose();
}
