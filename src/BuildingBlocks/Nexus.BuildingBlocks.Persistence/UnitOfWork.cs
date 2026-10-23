using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Npgsql;

namespace Nexus.BuildingBlocks.Persistence;

/// <inheritdoc cref="IUnitOfWork"/>
public sealed class UnitOfWork : IUnitOfWork
{
    private const string SessionContextSql =
        "SELECT set_config('app.current_user_id', $1, true), set_config('app.correlation_id', $2, true)";

    private readonly ServiceDataSource _dataSource;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationContext _correlation;
    private readonly IOutboxWriter _outboxWriter;

    private readonly List<DbContextBase> _dbContexts = [];
    private readonly List<object> _explicitAggregates = [];
    private readonly List<PendingDomainEvent> _explicitEvents = [];

    private State _state = State.NotStarted;
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private OutboxEnvelopeContext _envelope;

    public UnitOfWork(
        ServiceDataSource dataSource,
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        IOutboxWriter outboxWriter)
    {
        _dataSource = dataSource;
        _currentUser = currentUser;
        _correlation = correlation;
        _outboxWriter = outboxWriter;
    }

    private enum State
    {
        NotStarted,
        Active,
        Committed,
        RolledBack,
    }

    public bool IsActive => _state == State.Active;

    public NpgsqlConnection Connection =>
        IsActive ? _connection! : throw NotActive();

    public NpgsqlTransaction Transaction =>
        IsActive ? _transaction! : throw NotActive();

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_state != State.NotStarted)
        {
            throw new InvalidOperationException($"The unit of work is single-use and is already {_state}.");
        }

        // RLS context must identify a real user (BE-R1): an empty id would silently see nothing.
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("A unit of work requires an authenticated ICurrentUser with a non-empty UserId.");
        }

        if (_correlation.CorrelationId == Guid.Empty)
        {
            throw new InvalidOperationException("A unit of work requires ICorrelationContext.CorrelationId to be set.");
        }

        _envelope = new OutboxEnvelopeContext(
            _currentUser.UserId,
            _correlation.CorrelationId,
            _correlation.CausationId ?? _correlation.CorrelationId);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (var command = new NpgsqlCommand(SessionContextSql, connection, transaction))
            {
                command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = _envelope.ActorUserId.ToString("D") });
                command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = _envelope.CorrelationId.ToString("D") });
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _connection = connection;
        _transaction = transaction;
        _state = State.Active;
    }

    public void AddEvent(IDomainEvent domainEvent, long aggregateVersion)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        EnsureActive();
        _explicitEvents.Add(new PendingDomainEvent(domainEvent, aggregateVersion));
    }

    public void AddEventsFrom<TId>(AggregateRoot<TId> aggregate)
        where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        EnsureActive();
        if (!_explicitAggregates.Contains(aggregate, ReferenceEqualityComparer.Instance))
        {
            _explicitAggregates.Add(aggregate);
        }
    }

    public void Enlist(DbContextBase dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        if (_state is State.Committed or State.RolledBack)
        {
            throw new InvalidOperationException($"Cannot enlist a DbContext in a unit of work that is already {_state}.");
        }

        if (!_dbContexts.Contains(dbContext))
        {
            _dbContexts.Add(dbContext);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();

        List<object> aggregates;
        try
        {
            // 1. Snapshot aggregates BEFORE SaveChanges: deleted entities are detached by it,
            //    and their events (e.g. "deactivated") must not be lost.
            aggregates = new List<object>(_explicitAggregates);
            foreach (var dbContext in _dbContexts)
            {
                foreach (var entry in dbContext.ChangeTracker.Entries())
                {
                    if (AggregateAccessor.IsAggregate(entry.Entity.GetType())
                        && !aggregates.Contains(entry.Entity, ReferenceEqualityComparer.Instance))
                    {
                        aggregates.Add(entry.Entity);
                    }
                }
            }

            // 2. Business writes, on our connection and transaction.
            foreach (var dbContext in _dbContexts)
            {
                if (dbContext.Database.CurrentTransaction is null)
                {
                    await dbContext.Database.UseTransactionAsync(_transaction, cancellationToken).ConfigureAwait(false);
                }

                await dbContext.SaveChangesFromUnitOfWorkAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Outbox, same transaction (conventions-events.md §3). Validates EV-R2 and throws.
            var events = new List<PendingDomainEvent>();
            foreach (var aggregate in aggregates)
            {
                events.AddRange(AggregateAccessor.For(aggregate.GetType()).PendingEvents(aggregate));
            }

            events.AddRange(_explicitEvents);
            await _outboxWriter.WriteAsync(_connection!, _transaction!, events, _envelope, cancellationToken).ConfigureAwait(false);

            // 4. COMMIT.
            await _transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackCoreAsync(suppressErrors: true).ConfigureAwait(false);
            throw;
        }

        _state = State.Committed;
        await ReleaseAsync().ConfigureAwait(false);

        // Events are durable now; an aggregate reused in memory must not re-emit them.
        foreach (var aggregate in aggregates)
        {
            AggregateAccessor.For(aggregate.GetType()).Clear(aggregate);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await RollbackCoreAsync(suppressErrors: false).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_state == State.Active)
        {
            await RollbackCoreAsync(suppressErrors: true).ConfigureAwait(false);
        }

        await ReleaseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Rolls back and releases the connection. With <paramref name="suppressErrors"/> (failure
    /// and dispose paths) a failing ROLLBACK must not mask the original exception: closing the
    /// physical connection's transaction is guaranteed anyway, because Npgsql discards an open
    /// transaction when the connection returns to the pool (and the server aborts it if the
    /// connection is broken).
    /// </summary>
    private async Task RollbackCoreAsync(bool suppressErrors)
    {
        _state = State.RolledBack;
        try
        {
            // Not cancellable on purpose: a cancelled request must still release its locks.
            if (_transaction is { Connection: not null })
            {
                await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception) when (suppressErrors)
        {
            // See summary. The connection is disposed below either way.
        }
        finally
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
    }

    private async Task ReleaseAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw NotActive();
        }
    }

    private InvalidOperationException NotActive() =>
        new($"The unit of work is not active (state: {_state}). Call BeginAsync first; a unit of work is single-use.");

    /// <summary>
    /// Reflection bridge to <see cref="AggregateRoot{TId}"/>, which has no non-generic base.
    /// Cached per concrete type.
    /// </summary>
    private sealed class AggregateAccessor
    {
        private static readonly ConcurrentDictionary<Type, AggregateAccessor?> Cache = new();

        private readonly PropertyInfo _domainEvents;
        private readonly PropertyInfo _aggregateVersion;
        private readonly MethodInfo _clear;

        private AggregateAccessor(Type aggregateRootType)
        {
            _domainEvents = aggregateRootType.GetProperty(nameof(AggregateRoot<int>.DomainEvents))!;
            _aggregateVersion = aggregateRootType.GetProperty(nameof(AggregateRoot<int>.AggregateVersion))!;
            _clear = aggregateRootType.GetMethod(nameof(AggregateRoot<int>.ClearDomainEvents))!;
        }

        public static bool IsAggregate(Type type) => Get(type) is not null;

        public static AggregateAccessor For(Type type) =>
            Get(type) ?? throw new InvalidOperationException($"{type} is not an AggregateRoot<TId>.");

        /// <summary>
        /// Pending events in raise order. Event i of n was raised at version
        /// <c>AggregateVersion - n + i + 1</c> (RaiseEvent bumps the version once per event).
        /// </summary>
        public IEnumerable<PendingDomainEvent> PendingEvents(object aggregate)
        {
            var events = (IReadOnlyList<IDomainEvent>)_domainEvents.GetValue(aggregate)!;
            var version = (long)_aggregateVersion.GetValue(aggregate)!;
            for (var i = 0; i < events.Count; i++)
            {
                yield return new PendingDomainEvent(events[i], version - events.Count + i + 1);
            }
        }

        public void Clear(object aggregate) => _clear.Invoke(aggregate, null);

        private static AggregateAccessor? Get(Type type) =>
            Cache.GetOrAdd(type, static t =>
            {
                for (var current = t; current is not null; current = current.BaseType)
                {
                    if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
                    {
                        return new AggregateAccessor(current);
                    }
                }

                return null;
            });
    }
}
