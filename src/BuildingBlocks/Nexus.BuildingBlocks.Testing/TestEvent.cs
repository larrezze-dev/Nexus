using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Testing;

/// <summary>
/// Minimal <see cref="IDomainEvent"/>, reusable by any test of the outbox/<c>UnitOfWork</c>
/// plumbing (not a real domain event of any service). Its local Avro contract lives at
/// <c>contracts/events/_test/test/test-event.created.v1.avsc</c> (conventions-events.md §6),
/// resolved from <see cref="EventType"/> "test.test-event.created" + <see cref="SchemaVersion"/>
/// 1 by <c>FileSystemEventSchemaProvider</c> rooted at <c>contracts/events/_test</c> (see
/// <see cref="RepoRoot"/>). That root is a fixtures directory, not a real contract: nothing that
/// registers or validates the real <c>contracts/events/&lt;context&gt;/...</c> tree scans it.
/// </summary>
public sealed record TestEvent(Guid TestEventId, string Name) : IDomainEvent
{
    public string AggregateType => "test-event";

    public Guid AggregateId => TestEventId;

    public string EventType => "test.test-event.created";

    public int SchemaVersion => 1;

    public string Topic => "nexus.test.test-event.created";

    public Guid? LegalEntityId => null;
}
