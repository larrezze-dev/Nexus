namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// Correlation data of the current unit of work: where <see cref="IUnitOfWork"/> takes the
/// value of <c>app.correlation_id</c> (conventions-database.md §7) and the
/// <c>correlation_id</c> / <c>causation_id</c> envelope fields of every outbox message
/// (conventions-events.md §2).
///
/// Registered as a scoped service and populated by whoever starts the scope, before the unit
/// of work begins:
/// <list type="bullet">
///   <item>HTTP requests (step 0.5 middleware): correlation from the incoming correlation
///   header or a freshly generated UUIDv7; causation = the request id.</item>
///   <item>Kafka consumers (step 0.7): correlation copied from the incoming event's
///   <c>nexus-correlation-id</c>; causation = the incoming <c>event_id</c>.</item>
///   <item>Jobs: a new UUIDv7 per run.</item>
/// </list>
/// The unit of work refuses to begin with an empty <see cref="CorrelationId"/>: a missing
/// correlation is a wiring bug, not something to paper over with a random value.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>The correlation id of the end-to-end flow. Must not be <see cref="Guid.Empty"/>.</summary>
    Guid CorrelationId { get; }

    /// <summary>
    /// The id of the event or request that directly caused this unit of work. When
    /// <see langword="null"/>, the unit of work uses <see cref="CorrelationId"/> (the flow's
    /// originating request is its own cause).
    /// </summary>
    Guid? CausationId { get; }
}

/// <summary>Default mutable <see cref="ICorrelationContext"/>, registered per scope.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    public Guid CorrelationId { get; set; }

    public Guid? CausationId { get; set; }
}
