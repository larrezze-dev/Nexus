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

/// <summary>PENDING backlog as seen by the leader.</summary>
public readonly record struct PendingStats(long Count, DateTimeOffset? OldestCreatedAt);

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

    /// <summary>
    /// Number of <c>PENDING</c> rows and the <c>created_at</c> of the oldest one (null when none),
    /// both served by <c>ix_outbox_pending</c>.
    /// </summary>
    Task<PendingStats> GetPendingStatsAsync(CancellationToken cancellationToken);
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
    /// <paramref name="terminal"/>, else stays <c>PENDING</c>. Terminal only for a
    /// <see cref="PublishFailureKind.Row"/> failure at <c>attempts &gt;= MaxAttempts</c>.
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

/// <summary>
/// Class of a publish failure (decision of Luciano, 2026-11-07; conventions-events.md §4 step 5).
/// It decides what the batch does next and whether the row can ever become <c>FAILED</c>.
/// </summary>
public enum PublishFailureKind
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>
    /// Infrastructure, global: the broker or the Schema Registry is unreachable, timeouts, 5xx, and
    /// registry 401/403/408/429. The rest of the batch would fail the same way: the batch stops and
    /// the relay backs off. <c>attempts + 1</c>, never <c>FAILED</c>.
    /// </summary>
    InfraGlobal = 1,

    /// <summary>
    /// The row's topic cannot be produced to (does not exist, or no permission), detected by the
    /// metadata preflight or reported by the produce itself. Only that aggregate is skipped for
    /// the rest of the batch; other aggregates continue. <c>attempts + 1</c> only when the preflight
    /// (or the produce) actually asked the cluster — at most once per topic per negative TTL; rows
    /// whose topic is still negatively cached are skipped untouched (<see cref="PublishResult.Deferred"/>).
    /// Never <c>FAILED</c>: creating the topic or granting the ACL resumes publication in order.
    /// </summary>
    TopicUnreachable = 2,

    /// <summary>
    /// This row cannot be published: payload does not decode/encode, local <c>.avsc</c> missing,
    /// content not registered (40401/40403) or other registry 4xx (409, 422…), message too large,
    /// invalid headers, or any unexpected exception preparing the message. The aggregate is
    /// skipped for the rest of the batch; the row becomes <c>FAILED</c> at <c>MaxAttempts</c>.
    /// </summary>
    Row = 3,
}

/// <summary>Outcome of publishing one row.</summary>
/// <param name="Succeeded">The broker acknowledged the message (<c>acks=all</c>).</param>
/// <param name="Error">Failure description, stored (truncated) in <c>last_error</c>. Never contains payload values.</param>
/// <param name="Kind">The failure class (<see cref="PublishFailureKind.None"/> on success).</param>
/// <param name="CountsAsAttempt">
/// False for a <see cref="Deferred"/> row: its topic is still in the preflight's negative cache, so
/// nothing was asked of the cluster. The aggregate is skipped for the rest of the batch, but the
/// row is not updated (no <c>attempts + 1</c>, no <c>last_error</c>), nothing is logged or counted.
/// </param>
public readonly record struct PublishResult(bool Succeeded, string? Error, PublishFailureKind Kind, bool CountsAsAttempt = true)
{
    public static PublishResult Success { get; } = new(true, null, PublishFailureKind.None);

    /// <summary>
    /// The row's topic is known to be unreachable from a recent preflight (negative cache): skip
    /// its aggregate without recording an attempt. A real attempt is counted only when the
    /// preflight runs again, i.e. at most once per topic per negative TTL.
    /// </summary>
    public static PublishResult Deferred(string reason) => new(false, reason, PublishFailureKind.TopicUnreachable, CountsAsAttempt: false);

    public static PublishResult Failure(string error, PublishFailureKind kind)
    {
        if (kind == PublishFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "A failure needs a failure kind.");
        }

        return new(false, error, kind);
    }
}

/// <summary>
/// The payload or headers could not be prepared: local <c>.avsc</c> missing, schema not
/// registered, payload or headers invalid (<see cref="PublishFailureKind.Row"/>) or the registry
/// unavailable (<see cref="IsTransient"/>: <see cref="PublishFailureKind.InfraGlobal"/>).
/// </summary>
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

    /// <summary>True: <see cref="PublishFailureKind.InfraGlobal"/>; false: <see cref="PublishFailureKind.Row"/>.</summary>
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
