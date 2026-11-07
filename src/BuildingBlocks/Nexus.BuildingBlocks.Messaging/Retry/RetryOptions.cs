namespace Nexus.BuildingBlocks.Messaging.Retry;

/// <summary>
/// Retry settings of a consumer (conventions-events.md §7: 1 attempt + 3 retries, 200 ms / 1 s / 5 s).
/// <see cref="MaxAttempts"/> is the TOTAL number of handler invocations: default 4 = the first attempt
/// plus 3 retries. Between attempt n and n+1 the policy waits <c>Delays[n-1]</c>, so with the defaults
/// every delay is used once (200 ms, 1 s, 5 s = 6.2 s of back-off). If there are fewer delays than
/// gaps the last delay is reused.
/// <para>
/// When every attempt failed, the record goes to <c>{topic}.DLQ</c>. If that publish itself fails, the
/// consumer pauses its partitions and retries the DLQ publish with an exponential back-off from
/// <see cref="DlqPublishInitialBackoff"/> up to <see cref="DlqPublishMaxBackoff"/>, indefinitely (the
/// message is never skipped), until it succeeds or the host stops.
/// </para>
/// </summary>
public sealed class RetryOptions
{
    public int MaxAttempts { get; init; } = 4;

    public IReadOnlyList<TimeSpan> Delays { get; init; } =
        [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)];

    /// <summary>First wait after a failed DLQ publish; doubled after every further failure.</summary>
    public TimeSpan DlqPublishInitialBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Cap of the DLQ publish back-off.</summary>
    public TimeSpan DlqPublishMaxBackoff { get; init; } = TimeSpan.FromSeconds(30);
}
