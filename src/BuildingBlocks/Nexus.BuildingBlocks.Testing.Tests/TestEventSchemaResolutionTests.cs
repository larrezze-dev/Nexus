using Nexus.BuildingBlocks.Persistence.Outbox;
using Shouldly;

namespace Nexus.BuildingBlocks.Testing.Tests;

/// <summary>
/// Pure unit test (no Postgres needed): proves the convention
/// "EventType + SchemaVersion -&gt; .avsc path" (conventions-events.md §6) really resolves
/// <see cref="TestEvent"/> to the fixture file under <c>contracts/events/_test/</c>, and that the
/// file's contents actually validate a real <see cref="TestEvent"/> payload end to end.
/// </summary>
public sealed class TestEventSchemaResolutionTests
{
    private readonly FileSystemEventSchemaProvider _provider = new(RepoRoot.Find("contracts/events/_test"));

    [Fact]
    public void ResolvePath_MatchesContractsEventsUnderscoreTestLayout()
    {
        var path = _provider.ResolvePath("test.test-event.created", 1);

        path.Replace(Path.DirectorySeparatorChar, '/').ShouldEndWith("contracts/events/_test/test/test-event.created.v1.avsc");
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void GetSchema_LoadsTheRealAvscFile_AndSerializerValidatesAGenuinePayload()
    {
        var testEvent = new TestEvent(Guid.CreateVersion7(), "hello");

        var schema = _provider.GetSchema(testEvent);

        schema.Fullname.ShouldBe("ch.nussli.nexus.test.TestEventCreated");
        schema.Fields.Select(f => f.Name).ShouldBe(["testEventId", "name"], ignoreOrder: true);
    }
}
