namespace Nexus.OutboxRelay;

/// <summary>
/// One row of <c>outbox.message</c> as the relay reads it (conventions-events.md §3). Only
/// <see cref="Attempts"/> among the mutable columns is carried: the relay computes the next
/// value under the row lock of the batch.
/// </summary>
public sealed record OutboxMessage(
    Guid Id,
    string Topic,
    string AggregateType,
    Guid AggregateId,
    long AggregateVersion,
    string EventType,
    int SchemaVersion,
    string HeadersJson,
    string Payload,
    DateTimeOffset CreatedAt,
    int Attempts);

/// <summary>
/// Leader election of the relay (conventions-events.md §4.1): only one instance publishes.
/// </summary>
public interface IRelayLeadership
{
    /// <summary>
    /// Tries once to become the leader. Returns the lease when the lock was obtained, or
    /// <see langword="null"/> when another instance holds it (the caller retries every
    /// <see cref="RelayOptions.LeaderRetrySeconds"/>). Never blocks waiting for the lock.
    /// </summary>
    Task<IRelayLease?> TryAcquireAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A held leadership. Every outbox operation goes through the lease, so nothing can be read,
/// locked or marked without the leader lock (see <c>PostgresRelayLease</c> for how the lock is
/// confirmed per batch). Disposing it releases the lock.
/// </summary>
public interface IRelayLease : IAsyncDisposable
{
    /// <summary>
    /// Waits for a <c>NOTIFY outbox_new</c> or for <paramref name="timeout"/>, whichever comes
    /// first (conventions-events.md §4.2). Returns <see langword="true"/> when woken by a
    /// notification. Throws <see cref="RelayLeadershipLostException"/> if the lease's session is gone.
    /// </summary>
    Task<bool> WaitForWakeUpAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms the leader lock, opens a transaction and locks the next batch of publishable
    /// rows (conventions-events.md §4.3). Throws <see cref="RelayLeadershipLostException"/> when the
    /// lock cannot be confirmed: the caller must stop publishing immediately.
    /// </summary>
    Task<IOutboxBatch> BeginBatchAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most <paramref name="maxRows"/> <c>PUBLISHED</c> rows with
    /// <c>published_at &lt; cutoff</c> (conventions-events.md §4.6) and returns how many were
    /// deleted. Never touches PENDING or FAILED rows.
    /// </summary>
    Task<int> PurgePublishedAsync(DateTimeOffset cutoff, int maxRows, CancellationToken cancellationToken);

    /// <summary>Number of <c>PENDING</c> rows (served by <c>ix_outbox_pending</c>).</summary>
    Task<long> CountPendingAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One locked batch of <c>outbox.message</c> rows inside an open transaction. Marks are buffered
/// and written, together, by <see cref="CommitAsync"/>; disposing without committing rolls back
/// (rows stay <c>PENDING</c>, untouched).
/// </summary>
public interface IOutboxBatch : IAsyncDisposable
{
    /// <summary>Rows in publication order: <c>ORDER BY created_at, id</c>.</summary>
    IReadOnlyList<OutboxMessage> Messages { get; }

    /// <summary>
    /// Re-confirms, on the batch's own session, that the leader lock is still held. Called before
    /// every produce, so a leader whose session has died stops after at most the message in
    /// flight instead of running through the rest of its batch while a new leader starts.
    /// Throws <see cref="RelayLeadershipLostException"/>.
    /// </summary>
    Task ConfirmLeadershipAsync(CancellationToken cancellationToken);

    void MarkPublished(Guid id, DateTimeOffset publishedAt);

    /// <summary>
    /// Records a failed attempt: <c>attempts</c> becomes <paramref name="attempts"/> and
    /// <c>last_error</c> <paramref name="lastError"/>; the row becomes <c>FAILED</c> when
    /// <paramref name="terminal"/>, else stays <c>PENDING</c>.
    /// </summary>
    void MarkFailed(Guid id, int attempts, string lastError, bool terminal);

    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Publishes one outbox row to Kafka (conventions-events.md §4.4).</summary>
public interface IEventPublisher
{
    /// <summary>
    /// Encodes and produces <paramref name="message"/>, awaiting its delivery report. Never
    /// throws for publication failures: they are returned as a <see cref="PublishResult"/>
    /// so the batch can record them.
    /// </summary>
    Task<PublishResult> PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Turns the Avro-JSON payload of a row into the value bytes of the Kafka message (Confluent
/// wire format). Throws <see cref="PayloadEncodingException"/> on failure.
/// </summary>
public interface IPayloadEncoder
{
    Task<byte[]> EncodeAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>Outcome of publishing one row.</summary>
/// <param name="Succeeded">The broker acknowledged the message (<c>acks=all</c>).</param>
/// <param name="Error">Failure description, stored (truncated) in <c>last_error</c>.</param>
/// <param name="IsTransient">
/// The failure is about the infrastructure (Kafka or Schema Registry unreachable, timeouts), not
/// about this row: the rest of the batch would fail the same way, so the batch stops and the relay
/// backs off instead of burning one attempt of every row in it.
/// </param>
public readonly record struct PublishResult(bool Succeeded, string? Error, bool IsTransient)
{
    public static PublishResult Success { get; } = new(true, null, false);

    public static PublishResult Failure(string error, bool isTransient) => new(false, error, isTransient);
}

/// <summary>The payload could not be encoded (schema missing in the registry, payload does not match, registry down...).</summary>
public sealed class PayloadEncodingException : Exception
{
    public PayloadEncodingException()
    {
    }

    public PayloadEncodingException(string message)
        : base(message)
    {
    }

    public PayloadEncodingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PayloadEncodingException(string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }

    /// <summary>See <see cref="PublishResult.IsTransient"/>.</summary>
    public bool IsTransient { get; }
}

/// <summary>The leader lock is no longer confirmed; the instance must stop publishing and re-acquire.</summary>
public sealed class RelayLeadershipLostException : Exception
{
    public RelayLeadershipLostException()
    {
    }

    public RelayLeadershipLostException(string message)
        : base(message)
    {
    }

    public RelayLeadershipLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
