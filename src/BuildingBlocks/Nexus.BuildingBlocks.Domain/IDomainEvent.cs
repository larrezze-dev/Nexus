namespace Nexus.BuildingBlocks.Domain;

/// <summary>
/// A domain event raised by an <see cref="AggregateRoot{TId}"/>. Aggregates only accumulate
/// these in memory via <see cref="AggregateRoot{TId}.RaiseEvent"/>; they never publish them
/// directly (conventions-backend.md §3) - that is the job of the Outbox/relay, added in a
/// later step.
///
/// The envelope headers below are only the subset this step is responsible for. The remaining
/// A7 envelope fields (event_id, occurred_at, actor_user_id, correlation_id, causation_id) are
/// stamped by the OutboxWriter in a later step, not here.
/// </summary>
public interface IDomainEvent
{
    /// <summary>The aggregate type that raised the event, e.g. "asset_type".</summary>
    string AggregateType { get; }

    /// <summary>The id of the aggregate instance that raised the event.</summary>
    Guid AggregateId { get; }

    /// <summary>
    /// The event type, formatted as "&lt;context&gt;.&lt;aggregate&gt;.&lt;event&gt;"
    /// (conventions-events.md §5), e.g. "catalog.asset_type.created".
    /// </summary>
    string EventType { get; }

    /// <summary>The schema version of this event's payload, for consumers to branch on.</summary>
    int SchemaVersion { get; }

    /// <summary>
    /// The Kafka topic the event is produced to, formatted as
    /// "nexus.{context}.{aggregate}.{event}" (conventions-events.md §5).
    /// </summary>
    string Topic { get; }

    /// <summary>
    /// The legal entity the event is scoped to, if any. Nullable because not every event has
    /// legal-entity scope (e.g. cross-entity or reference-data events).
    /// </summary>
    Guid? LegalEntityId { get; }
}
