using Nexus.BuildingBlocks.Persistence.Outbox;
using Shouldly;

namespace Nexus.BuildingBlocks.Persistence.Tests;

public sealed class FileSystemEventSchemaProviderTests
{
    private readonly FileSystemEventSchemaProvider _provider = new(Path.Combine(AppContext.BaseDirectory, "Schemas"));

    [Fact]
    public void ResolvesPathByConvention()
    {
        _provider.ResolvePath("stock.movement.posted", 2)
            .ShouldBe(Path.Combine(AppContext.BaseDirectory, "Schemas", "stock", "movement.posted.v2.avsc"));
    }

    [Fact]
    public void LoadsAndCachesRecordSchema()
    {
        var e = new WidgetDeleted(Guid.NewGuid());
        var schema = _provider.GetSchema(e);
        schema.Fullname.ShouldBe("ch.nussli.nexus.test.WidgetDeleted");
        _provider.GetSchema(e).ShouldBeSameAs(schema);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a.b")]
    [InlineData("../../etc.passwd.x")]
    [InlineData("Stock.movement.posted")]
    [InlineData("stock.movement/../x.posted")]
    [InlineData("stock.movement.posted.extra")]
    public void MalformedEventTypes_AreRejected(string eventType)
    {
        Should.Throw<EventSchemaNotFoundException>(() => _provider.ResolvePath(eventType, 1));
    }

    [Fact]
    public void VersionBelowOne_IsRejected()
    {
        Should.Throw<EventSchemaNotFoundException>(() => _provider.ResolvePath("stock.movement.posted", 0));
    }
}
