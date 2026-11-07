namespace Nexus.BuildingBlocks.Messaging.Retry;

/// <summary>
/// Runs an attempt up to <see cref="RetryOptions.MaxAttempts"/> times (default 4: 1 attempt + 3 retries)
/// with TimeProvider-based backoff.
/// </summary>
public sealed class RetryPolicy(RetryOptions options, TimeProvider timeProvider)
{
    public RetryPolicy(TimeProvider timeProvider) : this(new RetryOptions(), timeProvider)
    {
    }

    /// <summary>
    /// Returns normally when an attempt succeeds, or after <paramref name="onExhausted"/> ran once with the last
    /// exception. Cancellation of <paramref name="ct"/> is never retried nor routed to onExhausted: it propagates.
    /// </summary>
    public async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> attempt,
        Func<Exception, Task> onExhausted,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(onExhausted);
        var max = Math.Max(1, options.MaxAttempts);

        for (var n = 1; ; n++)
        {
            try
            {
                await attempt(ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                if (n >= max)
                {
                    await onExhausted(ex).ConfigureAwait(false);
                    return;
                }

                var delays = options.Delays;
                if (delays.Count > 0)
                {
                    var delay = delays[Math.Min(n - 1, delays.Count - 1)];
                    await Task.Delay(delay, timeProvider, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
