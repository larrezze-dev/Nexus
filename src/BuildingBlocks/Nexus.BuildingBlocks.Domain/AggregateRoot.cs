namespace Nexus.BuildingBlocks.Domain;

/// <summary>
/// Base class for aggregate roots. Accumulates domain events raised while handling a command,
/// and tracks an in-memory version counter that later becomes the "nexus-aggregate-version"
/// header the OutboxWriter stamps on each outgoing message (conventions-events.md §2).
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identifier.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>
    /// The domain events raised so far and not yet cleared. Read-only from the outside: only
    /// the aggregate itself can add to this list, via <see cref="RaiseEvent"/>.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>
    /// Grows by one every time this aggregate raises an event. Consumed later by the
    /// OutboxWriter for the "nexus-aggregate-version" header.
    /// </summary>
    public long AggregateVersion { get; private set; }

    /// <summary>
    /// Accumulates <paramref name="domainEvent"/> for later dispatch by the UnitOfWork/Outbox
    /// and advances <see cref="AggregateVersion"/>. Never publishes directly to Kafka.
    /// </summary>
    protected void RaiseEvent(IDomainEvent domainEvent)
    {
        Guard.Against.Null(domainEvent, nameof(domainEvent));

        _domainEvents.Add(domainEvent);
        AggregateVersion++;
    }

    /// <summary>
    /// Empties the accumulated domain events. Called by the UnitOfWork after it has read them,
    /// so an aggregate reused in memory does not get its events reprocessed.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
