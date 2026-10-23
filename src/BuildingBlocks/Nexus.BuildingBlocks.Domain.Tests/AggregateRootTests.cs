using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Domain.Tests;

public sealed class AggregateRootTests
{
    [Fact]
    public void RaiseEvent_Accumulates_The_Event_And_Increments_The_Version()
    {
        var aggregate = new SampleAggregate(Guid.NewGuid());

        aggregate.Touch();

        aggregate.DomainEvents.Count.ShouldBe(1);
        aggregate.AggregateVersion.ShouldBe(1);
    }

    [Fact]
    public void RaiseEvent_Called_Multiple_Times_Keeps_Accumulating()
    {
        var aggregate = new SampleAggregate(Guid.NewGuid());

        aggregate.Touch();
        aggregate.Touch();
        aggregate.Touch();

        aggregate.DomainEvents.Count.ShouldBe(3);
        aggregate.AggregateVersion.ShouldBe(3);
    }

    [Fact]
    public void New_Aggregate_Has_No_Domain_Events_And_Version_Zero()
    {
        var aggregate = new SampleAggregate(Guid.NewGuid());

        aggregate.DomainEvents.ShouldBeEmpty();
        aggregate.AggregateVersion.ShouldBe(0);
    }

    [Fact]
    public void ClearDomainEvents_Empties_The_List_But_Keeps_The_Version()
    {
        var aggregate = new SampleAggregate(Guid.NewGuid());
        aggregate.Touch();
        aggregate.Touch();

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.ShouldBeEmpty();
        aggregate.AggregateVersion.ShouldBe(2);
    }
}
