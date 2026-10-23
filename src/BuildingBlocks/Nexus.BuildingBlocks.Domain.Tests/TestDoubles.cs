namespace Nexus.BuildingBlocks.Domain.Tests;

/// <summary>A minimal concrete <see cref="Entity{TId}"/> for exercising identity equality.</summary>
internal sealed class SampleEntity(Guid id) : Entity<Guid>(id);

/// <summary>A second entity type, used to prove that type matters for equality too.</summary>
internal sealed class OtherEntity(Guid id) : Entity<Guid>(id);

/// <summary>A minimal concrete <see cref="IDomainEvent"/> for exercising aggregate behavior.</summary>
internal sealed record SampleDomainEvent(Guid AggregateId) : IDomainEvent
{
    public string AggregateType => "sample_aggregate";

    public string EventType => "sample.sample_aggregate.touched";

    public int SchemaVersion => 1;

    public string Topic => "nexus.sample.sample_aggregate.touched";

    public Guid? LegalEntityId => null;
}

/// <summary>A minimal concrete <see cref="AggregateRoot{TId}"/> exposing <c>RaiseEvent</c> for tests.</summary>
internal sealed class SampleAggregate(Guid id) : AggregateRoot<Guid>(id)
{
    public void Touch() => RaiseEvent(new SampleDomainEvent(Id));
}
