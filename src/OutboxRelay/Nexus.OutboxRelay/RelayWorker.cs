using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nexus.OutboxRelay;

/// <summary>
/// The relay loop (conventions-events.md §4, ficha 0.7 B.1–B.5):
/// <list type="number">
///   <item>Try to become leader; a passive instance retries every
///   <see cref="RelayOptions.LeaderRetrySeconds"/>.</item>
///   <item>As leader: drain batches back to back while they come back full; otherwise wait for
///   <c>NOTIFY outbox_new</c> or <see cref="RelayOptions.PollIntervalMs"/>, whichever comes first.</item>
///   <item>After a transient failure (Kafka / Schema Registry down) back off exponentially from
///   the poll interval up to <see cref="RelayOptions.MaxBackoffSeconds"/>, so an outage does not
///   hammer the cluster. The head row's <c>attempts</c> grow by one per step, but a transient
///   failure never makes it <c>FAILED</c> (D-H1).</item>
///   <item>Any failure of the leader session (connection lost, lock not confirmed) ends the
///   lease at once: nothing is published until the lock is acquired again.</item>
/// </list>
/// </summary>
public sealed partial class RelayWorker : BackgroundService
{
    private readonly IRelayLeadership _leadership;
    private readonly OutboxBatchProcessor _processor;
    private readonly OutboxMaintenance _maintenance;
    private readonly RelayMetrics _metrics;
    private readonly RelayOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RelayWorker> _logger;

    public RelayWorker(
        IRelayLeadership leadership,
        IEventPublisher publisher,
        RelayMetrics metrics,
        IOptions<RelayOptions> options,
        TimeProvider timeProvider,
        ILogger<RelayWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _leadership = leadership;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _metrics = metrics;
        _maintenance = new OutboxMaintenance(metrics, _options, timeProvider, logger);
        _processor = new OutboxBatchProcessor(publisher, metrics, timeProvider, _options.MaxAttempts, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger);
        var leaderRetry = TimeSpan.FromSeconds(_options.LeaderRetrySeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            IRelayLease? lease = null;
            try
            {
                lease = await _leadership.TryAcquireAsync(stoppingToken).ConfigureAwait(false);
                if (lease is null)
                {
                    await Task.Delay(leaderRetry, _timeProvider, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await RunAsLeaderAsync(lease, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive anything: it logs, drops the lease and re-elects.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogLeadershipLost(_logger, ex.GetType().Name, ex.Message);
                await DelayQuietlyAsync(leaderRetry, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                if (lease is not null)
                {
                    // No longer the leader: stop reporting the pending gauge (H7).
                    _metrics.ClearPending();
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        LogStopped(_logger);
    }

    /// <summary>The leader loop; returns only on shutdown, throws when the lease is lost.</summary>
    internal async Task RunAsLeaderAsync(IRelayLease lease, CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(_options.PollIntervalMs);
        var maxBackoff = TimeSpan.FromSeconds(_options.MaxBackoffSeconds);
        var backoff = TimeSpan.Zero;
        var drainNow = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (backoff > TimeSpan.Zero)
            {
                // Not interruptible by NOTIFY: new rows do not make the cluster come back sooner.
                await Task.Delay(backoff, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            else if (!drainNow)
            {
                await lease.WaitForWakeUpAsync(pollInterval, stoppingToken).ConfigureAwait(false);
            }

            // Leader-only housekeeping (daily purge, pending gauge); never fails the loop.
            await _maintenance.RunAsync(lease, stoppingToken).ConfigureAwait(false);

            BatchOutcome outcome;
            await using (var batch = await lease.BeginBatchAsync(_options.BatchSize, stoppingToken).ConfigureAwait(false))
            {
                outcome = await _processor.ProcessAsync(batch, stoppingToken).ConfigureAwait(false);
            }

            if (outcome.StoppedOnTransientFailure)
            {
                backoff = backoff == TimeSpan.Zero ? pollInterval : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
                LogBackingOff(_logger, backoff.TotalMilliseconds);
            }
            else
            {
                backoff = TimeSpan.Zero;
                // A full batch means there is probably more: go again without waiting.
                drainNow = outcome.Selected >= _options.BatchSize;
            }
        }
    }

    private async Task DelayQuietlyAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox relay started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox relay stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relay leadership lost or not obtainable ({ExceptionType}: {Reason}); publishing stopped until the lock is re-acquired.")]
    private static partial void LogLeadershipLost(ILogger logger, string exceptionType, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka or Schema Registry unavailable; next batch in {BackoffMs} ms.")]
    private static partial void LogBackingOff(ILogger logger, double backoffMs);
}
