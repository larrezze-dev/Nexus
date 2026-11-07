using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;
using Npgsql;

namespace Nexus.BuildingBlocks.Messaging.Inbox;

/// <summary>
/// Consumer idempotency (conventions-events.md §7): the effect of an event and its row in
/// <c>inbox.processed_message</c> commit in the same transaction, and an <c>event_id</c> this
/// consumer group already processed is skipped.
/// <para>
/// <b>Scope and lifetime.</b> Single-use, like the <see cref="IUnitOfWork"/> it drives: the consumer
/// creates one DI scope per attempt, instantiates this processor in it with
/// <c>ActivatorUtilities.CreateInstance</c>, resolves every service its effect needs from <b>that same
/// scope</b>, and disposes the scope afterwards.
/// </para>
/// <para>
/// <b>Not a DI service (ADR-0017, risk of option A).</b> The processor switches the scope's
/// <see cref="ICurrentUser"/> to the event's identity, so it is internal and never registered:
/// application code (an HTTP endpoint, a handler) cannot resolve it and impersonate a user. As defence
/// in depth it refuses to run in a scope that already has an authenticated user or an
/// <see cref="HttpContext"/>, and it clears the consumer identity before returning.
/// </para>
/// <para>
/// <b>How the effect reaches the transaction.</b> The effect is a closure over services resolved
/// from the message's scope. The scope's <see cref="IUnitOfWork"/> / <see cref="IRequestTransaction"/>
/// (one scoped instance) is already active when the effect runs, so:
/// <list type="bullet">
///   <item>Dapper/Npgsql code uses <see cref="IUnitOfWork.Connection"/> and <see cref="IUnitOfWork.Transaction"/>;</item>
///   <item>a <c>DbContextBase</c> resolved from the scope enlists itself and is saved on commit;</item>
///   <item>a command handler that calls <c>BeginAsync</c> gets the already-open transaction
///   (a no-op while active) and its <c>CommitAsync</c> commits the inbox row with it.</item>
/// </list>
/// </para>
/// <para>
/// <b>Effect contract.</b> Returning normally means "done": if the transaction is still active
/// the processor commits it (effect + inbox row + outbox). Throwing means "not done": the
/// processor rolls everything back (inbox row included) and rethrows, so the offset is not
/// committed and the retry/DLQ policy decides. An effect that dispatches a command handler must
/// turn a failed <c>Result</c> into an exception: returning normally would commit whatever the
/// handler wrote before failing. If the effect ends the transaction itself, the processor checks
/// that the inbox row was actually committed and throws if it was not (a rollback inside the
/// effect is never reported as <see cref="InboxResult.Processed"/>).
/// </para>
/// <para>
/// <b>Concurrency.</b> The inbox row is inserted first, with
/// <c>ON CONFLICT (consumer, event_id) DO NOTHING</c>. Two concurrent deliveries of the same event
/// to the same group serialize on the primary key: the second <c>INSERT</c> waits for the first
/// transaction; if it commits, the second inserts nothing and skips the effect; if it rolls back,
/// the second inserts and runs the effect. Exactly one effect commits, without explicit locks.
/// </para>
/// </summary>
internal interface IInboxProcessor
{
    /// <summary>
    /// Runs <paramref name="effect"/> at most once per (<paramref name="consumerGroup"/>,
    /// <c>event_id</c>), in one transaction with the inbox row, under the identity chosen by
    /// <paramref name="identity"/> (<c>app.current_user_id</c>) and with
    /// <c>app.correlation_id</c> = the event's correlation id; events emitted by the effect get
    /// causation id = this event's id.
    /// </summary>
    Task<InboxResult> ProcessOnceAsync(
        string consumerGroup,
        EventEnvelope envelope,
        ActingIdentity identity,
        Func<CancellationToken, Task> effect,
        CancellationToken ct);
}

/// <inheritdoc cref="IInboxProcessor"/>
internal sealed partial class InboxProcessor : IInboxProcessor
{
    private const string InsertSql =
        "INSERT INTO inbox.processed_message (consumer, event_id, processed_at) VALUES ($1, $2, $3) " +
        "ON CONFLICT (consumer, event_id) DO NOTHING";

    private const string ExistsSql =
        "SELECT EXISTS (SELECT 1 FROM inbox.processed_message WHERE consumer = $1 AND event_id = $2)";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ConsumerIdentity _consumerIdentity;
    private readonly ICurrentUser _currentUser;
    private readonly CorrelationContext _correlation;
    private readonly ICorrelationContext _correlationSeenByUnitOfWork;
    private readonly ServiceDataSource _dataSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InboxProcessor> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    private bool _used;

    public InboxProcessor(
        IUnitOfWork unitOfWork,
        ConsumerIdentity consumerIdentity,
        ICurrentUser currentUser,
        CorrelationContext correlation,
        ICorrelationContext correlationSeenByUnitOfWork,
        ServiceDataSource dataSource,
        TimeProvider timeProvider,
        ILogger<InboxProcessor> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _consumerIdentity = consumerIdentity;
        _currentUser = currentUser;
        _correlation = correlation;
        _correlationSeenByUnitOfWork = correlationSeenByUnitOfWork;
        _dataSource = dataSource;
        _timeProvider = timeProvider;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<InboxResult> ProcessOnceAsync(
        string consumerGroup,
        EventEnvelope envelope,
        ActingIdentity identity,
        Func<CancellationToken, Task> effect,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerGroup);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(effect);
        if (envelope.EventId == Guid.Empty)
        {
            throw new ArgumentException("The event id must not be the empty UUID.", nameof(envelope));
        }

        if (_used)
        {
            throw new InvalidOperationException("An InboxProcessor is single-use: create one DI scope per consumed message.");
        }

        _used = true;

        if (_unitOfWork.IsActive)
        {
            // Someone already began this scope's transaction under another identity: the inbox
            // row and the effect would not run as the event's identity.
            throw new InvalidOperationException("The scope's unit of work is already active; the inbox processor must begin it.");
        }

        if (_httpContextAccessor?.HttpContext is not null)
        {
            // A consumer never runs inside an HTTP request; doing so would let a request act as
            // another user (ADR-0017).
            throw new InvalidOperationException(
                "The inbox processor must not run inside an HTTP request (an HttpContext is present).");
        }

        if (_currentUser.IsAuthenticated)
        {
            // Before the consumer identity is set the scope's ICurrentUser is the host's own one
            // (e.g. HttpCurrentUser); a consumer scope has nobody authenticated. Anything else is a
            // request scope or a miswired host: never replace a real user with the event's identity.
            throw new InvalidOperationException(
                "The scope already has an authenticated ICurrentUser; the inbox processor only runs in a consumer's own DI scope.");
        }

        var userId = ResolveUserId(envelope, identity);
        _consumerIdentity.Set(userId);
        try
        {
            return await ProcessUnderConsumerIdentityAsync(consumerGroup, envelope, userId, effect, ct).ConfigureAwait(false);
        }
        finally
        {
            // Whatever happened, the scope stops acting as the event's identity.
            _consumerIdentity.Clear();
        }
    }

    private async Task<InboxResult> ProcessUnderConsumerIdentityAsync(
        string consumerGroup,
        EventEnvelope envelope,
        Guid userId,
        Func<CancellationToken, Task> effect,
        CancellationToken ct)
    {
        if (_currentUser.UserId != userId || !_currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException(
                "The scope's ICurrentUser does not reflect the consumer identity. Call AddNexusInbox() after " +
                "every other ICurrentUser registration (e.g. AddNexusAuthentication).");
        }

        if (!ReferenceEquals(_correlation, _correlationSeenByUnitOfWork))
        {
            throw new InvalidOperationException(
                "ICorrelationContext must resolve to the scope's CorrelationContext instance for the inbox to set it.");
        }

        _correlation.CorrelationId = envelope.CorrelationId;
        _correlation.CausationId = envelope.EventId;

        await _unitOfWork.BeginAsync(ct).ConfigureAwait(false);
        try
        {
            if (!await TryInsertInboxRowAsync(consumerGroup, envelope.EventId, ct).ConfigureAwait(false))
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                LogAlreadyProcessed(_logger, consumerGroup, envelope.EventId);
                return InboxResult.AlreadyProcessed;
            }

            await effect(ct).ConfigureAwait(false);
        }
        catch
        {
            await RollbackQuietlyAsync().ConfigureAwait(false);
            throw;
        }

        if (_unitOfWork.IsActive)
        {
            // Rolls back by itself and rethrows on failure.
            await _unitOfWork.CommitAsync(ct).ConfigureAwait(false);
            return InboxResult.Processed;
        }

        // The effect committed or rolled back the transaction itself. Only a committed inbox row
        // proves the effect is durable; anything else must not be acknowledged.
        if (await IsInboxRowCommittedAsync(consumerGroup, envelope.EventId).ConfigureAwait(false))
        {
            return InboxResult.Processed;
        }

        throw new InvalidOperationException(
            $"The effect of consumer '{consumerGroup}' for event {envelope.EventId} ended the transaction without committing it; " +
            "the event was not recorded as processed. Throw from the effect to signal failure.");
    }

    private Guid ResolveUserId(EventEnvelope envelope, ActingIdentity identity)
    {
        switch (identity)
        {
            case ActingIdentity.System:
                return SystemUser.Id;
            case ActingIdentity.OnBehalfOfActor:
                if (envelope.ActorUserId is { } actor && actor != Guid.Empty)
                {
                    return actor;
                }

                LogActorMissing(_logger, envelope.EventId, envelope.EventType);
                return SystemUser.Id;
            default:
                throw new ArgumentOutOfRangeException(nameof(identity), identity, "Unknown acting identity.");
        }
    }

    private async Task<bool> TryInsertInboxRowAsync(string consumerGroup, Guid eventId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(InsertSql, _unitOfWork.Connection, _unitOfWork.Transaction);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = consumerGroup });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = _timeProvider.GetUtcNow() });
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Read on a separate connection, after the unit of work ended: sees only committed rows.
    /// <c>inbox.processed_message</c> has no RLS (conventions-database.md §2), so no session
    /// context is needed; the service role holds SELECT on it.
    /// </summary>
    private async Task<bool> IsInboxRowCommittedAsync(string consumerGroup, Guid eventId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(ExistsSql, connection);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = consumerGroup });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
        return (bool)(await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false))!;
    }

    private async Task RollbackQuietlyAsync()
    {
        if (!_unitOfWork.IsActive)
        {
            return;
        }

        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Must not mask the effect's exception. The unit of work released the connection
            // anyway, and Npgsql/PostgreSQL discard an open transaction with it.
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consumer {ConsumerGroup} already processed event {EventId}; skipped")]
    private static partial void LogAlreadyProcessed(ILogger logger, string consumerGroup, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Event {EventId} ({EventType}) has no actor_user_id; consuming on behalf of the actor falls back to SYSTEM")]
    private static partial void LogActorMissing(ILogger logger, Guid eventId, string eventType);
}
