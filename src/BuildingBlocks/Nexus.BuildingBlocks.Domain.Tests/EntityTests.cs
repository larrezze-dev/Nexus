using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Domain.Tests;

public sealed class EntityTests
{
    [Fact]
    public void Entities_With_The_Same_Id_Are_Equal_Even_As_Different_Instances()
    {
        var id = Guid.NewGuid();
        var first = new SampleEntity(id);
        var second = new SampleEntity(id);

        first.Equals(second).ShouldBeTrue();
        (first == second).ShouldBeTrue();
        (first != second).ShouldBeFalse();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Entities_With_Different_Ids_Are_Not_Equal()
    {
        var first = new SampleEntity(Guid.NewGuid());
        var second = new SampleEntity(Guid.NewGuid());

        first.Equals(second).ShouldBeFalse();
        (first == second).ShouldBeFalse();
        (first != second).ShouldBeTrue();
    }

    [Fact]
    public void Entities_Of_Different_Types_With_The_Same_Id_Are_Not_Equal()
    {
        var id = Guid.NewGuid();
        var entity = new SampleEntity(id);
        var other = new OtherEntity(id);

        entity.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void Entity_Is_Equal_To_Itself()
    {
        var entity = new SampleEntity(Guid.NewGuid());

        entity.Equals(entity).ShouldBeTrue();
    }

    [Fact]
    public void Null_Comparisons_Do_Not_Throw_And_Are_Not_Equal()
    {
        SampleEntity? entity = new(Guid.NewGuid());
        SampleEntity? nullEntity = null;

        entity.Equals(null).ShouldBeFalse();
        (nullEntity == null).ShouldBeTrue();
        (entity == null).ShouldBeFalse();
        (null == entity).ShouldBeFalse();
    }
}
