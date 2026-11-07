using System.Collections.Concurrent;

namespace Nexus.BuildingBlocks.Messaging.Health;

/// <summary>Lifecycle of one consumer loop, as seen by the health check.</summary>
public enum ConsumerLoopState
{
    /// <summary>Registered; the loop has not subscribed yet.</summary>
    Starting,

    /// <summary>Subscribed and consuming.</summary>
    Running,

    /// <summary>
    /// The loop is alive but the last poll failed with a non-fatal Kafka error (e.g. the topic does not
    /// exist yet, a broker is unreachable). It keeps polling with a short back-off and returns to
    /// <see cref="Running"/> on the next successful poll. Health: <c>Degraded</c>.
    /// </summary>
    Degraded,

    /// <summary>
    /// A record exhausted its retries and could not be written to its DLQ. Consumption is paused (no
    /// commit, no further records) while the DLQ publish is retried with back-off; it resumes when the
    /// publish succeeds. Health: <c>Unhealthy</c>.
    /// </summary>
    DlqUnavailable,

    /// <summary>Stopped by a host shutdown (cancellation). Not a failure.</summary>
    Stopped,

    /// <summary>The loop died with an exception: nothing is being consumed for this group.</summary>
    Faulted,
}

/// <summary>Snapshot of one consumer's loop.</summary>
public sealed record ConsumerStatus(
    string GroupId,
    string Topic,
    ConsumerLoopState State,
    string? Error,
    DateTimeOffset ChangedAt);

/// <summary>
/// Singleton that every <see cref="NexusConsumer{TEvent}"/> of the host reports its loop state to.
/// The health check reads it (<see cref="ConsumerLagHealthCheck"/>). Thread-safe.
/// </summary>
public sealed class ConsumerHealthRegistry(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, ConsumerStatus> _consumers = new(StringComparer.Ordinal);

    /// <summary>Registers a consumer as <see cref="ConsumerLoopState.Starting"/>.</summary>
    /// <exception cref="InvalidOperationException">Two consumers of the host share a group id.</exception>
    public void Register(string groupId, string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        var status = new ConsumerStatus(groupId, topic, ConsumerLoopState.Starting, null, timeProvider.GetUtcNow());
        _consumers.AddOrUpdate(
            groupId,
            status,
            (_, current) => current.State == ConsumerLoopState.Stopped
                ? status // a new instance replacing one that shut down cleanly
                         // Two live classes with the same <service>.<use-case> would split the topic's
                         // partitions between them and each would see only part of the events.
                : throw new InvalidOperationException($"Two consumers of this host use the consumer group '{groupId}'."));
    }

    public void Report(string groupId, ConsumerLoopState state, Exception? error = null)
    {
        _consumers.AddOrUpdate(
            groupId,
            _ => throw new InvalidOperationException($"Consumer group '{groupId}' is not registered."),
            (_, current) => current with
            {
                State = state,
                Error = error is null ? null : $"{error.GetType().FullName}: {error.Message}",
                ChangedAt = timeProvider.GetUtcNow(),
            });
    }

    public IReadOnlyList<ConsumerStatus> Snapshot() =>
        _consumers.Values.OrderBy(c => c.GroupId, StringComparer.Ordinal).ToList();
}
