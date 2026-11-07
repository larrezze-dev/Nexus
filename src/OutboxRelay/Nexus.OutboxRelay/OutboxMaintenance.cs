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
    /// Returns the rows deleted (0 when not due).
    /// </summary>
    public async Task<long> PurgeIfDueAsync(IRelayLease lease, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_lastPurge is { } last && now - last < TimeSpan.FromHours(_options.PurgeIntervalHours))
        {
            return 0;
        }

        _lastPurge = now; // Also on failure: retry at the next interval, not in a tight loop.
        var cutoff = now - TimeSpan.FromDays(_options.PurgeRetentionDays);
        long total = 0;
        try
        {
            int deleted;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                deleted = await lease.PurgePublishedAsync(cutoff, _options.PurgeChunkSize, cancellationToken).ConfigureAwait(false);
                total += deleted;
                if (deleted > 0)
                {
                    _metrics.Purged(deleted);
                }
            }
            while (deleted >= _options.PurgeChunkSize);

            LogPurged(_logger, total);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox housekeeping step '{Step}' failed ({ExceptionType}); will retry later.")]
    private static partial void LogHousekeepingFailed(ILogger logger, string step, string exceptionType);
}
