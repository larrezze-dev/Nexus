using Nexus.BuildingBlocks.Testing;
using Nexus.Projections.Projections;
using Shouldly;

namespace Nexus.Projections.Tests.Projections;

public sealed class TopicRegistryTests
{
    private const string Valid = """
        # comment
        topics:
          - name: nexus.org.party.created   # trailing comment
            context: org
            partitions:
              local: 1
              azure: 6
            pii: true
            owner: nexus-platform
          - name: "nexus.org.warehouse.created"
            pii: false
        """;

    [Fact(DisplayName = "EV-R5: parses name and pii, ignoring comments, other keys and the nested partitions map")]
    public void Parses_known_structure()
    {
        var r = TopicRegistry.Parse(Valid);
        r.Count.ShouldBe(2);
        r.IsPii("nexus.org.party.created").ShouldBeTrue();
        r.IsPii("nexus.org.warehouse.created").ShouldBeFalse();
        r.IsKnown("nexus.org.warehouse.created").ShouldBeTrue();
    }

    [Fact(DisplayName = "EV-R5: a topic missing from the registry is treated as PII (fail closed)")]
    public void Unknown_topic_is_pii()
    {
        var r = TopicRegistry.Parse(Valid);
        r.IsKnown("nexus.other.thing.done").ShouldBeFalse();
        r.IsPii("nexus.other.thing.done").ShouldBeTrue();
    }

    [Theory(DisplayName = "EV-R5: malformed registry fails fast")]
    [InlineData("topics:\n  - context: org\n    pii: true\n", "without 'name'")]
    [InlineData("topics:\n  - name: a.b.c.d\n    context: org\n", "pii")]
    [InlineData("topics:\n  - name: a.b.c.d\n    pii: yes\n", "pii")]
    [InlineData("topics:\n  - name: a.b.c.d\n    pii: true\n  - name: a.b.c.d\n    pii: false\n", "duplicate")]
    [InlineData("topics:\n", "no topics")]
    [InlineData("# nothing\n", "no topics")]
    public void Malformed_fails(string yaml, string expected)
    {
        var ex = Should.Throw<TopicRegistryException>(() => TopicRegistry.Parse(yaml));
        ex.Message.ShouldContain(expected);
    }

    [Fact(DisplayName = "EV-R5: missing file fails fast")]
    public void Missing_file_fails() =>
        Should.Throw<TopicRegistryException>(() => TopicRegistry.LoadFile("/nonexistent/topics.yaml"));

    [Fact(DisplayName = "EV-R5: real contracts/events/topics.yaml parses; party and user are PII, org structural topics are not")]
    public void Real_file_parses()
    {
        var r = TopicRegistry.LoadFile(RepoRoot.Find("contracts/events/topics.yaml"));
        r.Count.ShouldBeGreaterThan(0);
        r.IsPii("nexus.org.party.created").ShouldBeTrue();
        r.IsPii("nexus.org.user.created").ShouldBeTrue();
        r.IsPii("nexus.org.warehouse.updated").ShouldBeFalse();
        r.IsPii("nexus.org.legal-entity.created").ShouldBeFalse();
    }

    [Fact(DisplayName = "The copy of topics.yaml next to the binaries (default path) parses")]
    public void Default_path_parses() => TopicRegistry.LoadFile().Count.ShouldBeGreaterThan(0);
}
