using Microsoft.Extensions.Logging;

namespace Nexus.OutboxRelay;

/// <summary>
/// Leader-only housekeeping run between batches (conventions-events.md §4.6): the daily purge of
/// <c>PUBLISHED</c> rows older than <see cref="RelayOptions.PurgeRetentionDays"/>, and the refresh
/// of the <c>nexus_outbox_pending</c> gauge. It only ever runs through the leader's lease, so a
/// passive instance never purges. Failures are logged and swallowed: housekeeping must not stop
/// publication (a dead session surfaces on the next batch as a lost lease).
/// </summary>
public sealed partial class OutboxMaintenance
{
    /// <summary>Minimum time between two refreshes of the pending gauge.</summary>
    public static readonly TimeSpan PendingRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly RelayMetrics _metrics;
    private readonly RelayOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private DateTimeOffset? _lastPurge;
    private DateTimeOffset? _lastPendingRefresh;
    private bool _purgeInProgress;

    public OutboxMaintenance(RelayMetrics metrics, RelayOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _metrics = metrics;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunAsync(IRelayLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await RefreshPendingAsync(lease, cancellationToken).ConfigureAwait(false);
        await PurgeIfDueAsync(lease, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refreshes the gauge when the last refresh is older than <see cref="PendingRefreshInterval"/>.</summary>
    public async Task RefreshPendingAsync(IRelayLease lease, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_lastPendingRefresh is { } last && now - last < PendingRefreshInterval)
        {
            return;
        }

        try
        {
            _metrics.SetPending(await lease.CountPendingAsync(cancellationToken).ConfigureAwait(false));
            _lastPendingRefresh = now;
        }
#pragma warning disable CA1031 // Housekeeping never stops publication.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _lastPendingRefresh = now;
            LogHousekeepingFailed(_logger, "pending count", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Deletes PUBLISHED rows with <c>published_at</c> before now minus the retention, in chunks of
    /// <see cref="RelayOptions.PurgeChunkSize"/>, at most once per <see cref="RelayOptions.PurgeIntervalHours"/>.
    /// One call runs at most <see cref="RelayOptions.PurgeMaxChunksPerPass"/> chunks, so a large
    /// backlog never holds publication for long: when the cap is reached with more rows left, the
    /// purge stays "in progress" and the next call (the next pass of the leader loop, after a batch)
    /// resumes it without waiting for the interval. Returns the rows deleted by this call (0 when
    /// not due).
    /// </summary>
    public async Task<long> PurgeIfDueAsync(IRelayLease lease, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (!_purgeInProgress && _lastPurge is { } last && now - last < TimeSpan.FromHours(_options.PurgeIntervalHours))
        {
            return 0;
        }

        if (!_purgeInProgress)
        {
            _lastPurge = now; // Also on failure: retry at the next interval, not in a tight loop.
        }

        _purgeInProgress = false;
        var cutoff = now - TimeSpan.FromDays(_options.PurgeRetentionDays);
        var maxChunks = Math.Max(1, _options.PurgeMaxChunksPerPass);
        long total = 0;
        try
        {
            for (var chunk = 0; chunk < maxChunks; chunk++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deleted = await lease.PurgePublishedAsync(cutoff, _options.PurgeChunkSize, cancellationToken).ConfigureAwait(false);
                total += deleted;
                if (deleted > 0)
                {
                    _metrics.Purged(deleted);
                }

                if (deleted < _options.PurgeChunkSize)
                {
                    LogPurged(_logger, total);
                    return total;
                }
            }

            // Cap reached and the last chunk was full: probably more left, resume on the next pass.
            _purgeInProgress = true;
            LogPurgePaused(_logger, total);
        }
#pragma warning disable CA1031 // Housekeeping never stops publication.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogHousekeepingFailed(_logger, "purge", ex.GetType().Name);
        }

        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox purge deleted {Rows} PUBLISHED rows.")]
    private static partial void LogPurged(ILogger logger, long rows);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox purge deleted {Rows} PUBLISHED rows in this pass; more remain, resuming on the next pass.")]
    private static partial void LogPurgePaused(ILogger logger, long rows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox housekeeping step '{Step}' failed ({ExceptionType}); will retry later.")]
    private static partial void LogHousekeepingFailed(ILogger logger, string step, string exceptionType);
}
