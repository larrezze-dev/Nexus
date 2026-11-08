using System.Net;
using System.Net.Sockets;
using Confluent.SchemaRegistry;
using Nexus.Projections.Abstractions;
using Nexus.Projections.Migrations;
using Nexus.Projections.Runtime;
using Nexus.Projections.Tests.Runtime.Infrastructure;
using Shouldly;

namespace Nexus.Projections.Tests.Runtime;

/// <summary>Pure pieces of the runtime: statement splitting, topic matching, failure classes, catalog, CLI usage.</summary>
public sealed class RuntimeUnitTests
{
    private const string EventLogPattern = @"^nexus\.[a-z0-9-]+\.[a-z0-9-]+\.[a-z0-9-]+$";

    [Fact(DisplayName = "CH migrations: split on ';' drops empty and comment-only fragments, keeps statements with leading comments")]
    public void Split_drops_empty_and_comment_only_fragments()
    {
        const string sql = "-- head\nCREATE DATABASE IF NOT EXISTS x;\n\n;  ;\n-- only a comment\n;\n-- lead\nCREATE TABLE x.t (a UInt8) ENGINE = Memory;\n-- tail\n";

        var statements = ClickHouseMigrationScript.SplitStatements(sql);

        statements.Count.ShouldBe(2);
        statements[0].ShouldEndWith("CREATE DATABASE IF NOT EXISTS x");
        statements[1].ShouldStartWith("-- lead");
        statements[1].ShouldEndWith("ENGINE = Memory");
    }

    [Theory(DisplayName = "CH migrations: a malformed file name refuses to start")]
    [InlineData("V1_base.sql")]
    [InlineData("Vabc__base.sql")]
    [InlineData("V202611082100__bad-name.sql")]
    public void Malformed_file_name_refuses(string fileName)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-migrations", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, fileName), "SELECT 1;");
            Should.Throw<ClickHouseMigrationException>(() => ClickHouseMigrationScript.LoadDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory(DisplayName = "event_log pattern (.NET side): three segments after nexus. match; .DLQ, internal and foreign topics do not")]
    [InlineData("nexus.org.legal-entity.created", true)]
    [InlineData("nexus.stock.stock-movement.posted", true)]
    [InlineData("nexus.org.legal-entity.created.DLQ", false)]
    [InlineData("nexus.x.y.z.DLQ", false)]
    [InlineData("nexus.org.legal-entity", false)]
    [InlineData("_schemas", false)]
    [InlineData("__consumer_offsets", false)]
    [InlineData("test.org.thing.created", false)]
    public void Pattern_matches_like_librdkafka(string topic, bool expected) =>
        new ProjectionTopicMatcher(ProjectionSubscription.ForPattern(EventLogPattern)).Matches(topic).ShouldBe(expected);

    [Fact(DisplayName = "Topic matcher: a pattern that would let .DLQ through still never selects a DLQ topic")]
    public void Dlq_never_matches_even_with_loose_pattern()
    {
        var matcher = new ProjectionTopicMatcher(ProjectionSubscription.ForPattern(@"^nexus\..*"));
        matcher.Matches("nexus.org.party.created").ShouldBeTrue();
        matcher.Matches("nexus.org.party.created.DLQ").ShouldBeFalse();
    }

    [Fact(DisplayName = "Topic matcher: a topic list selects exactly its topics")]
    public void Topic_list_matches_exactly()
    {
        var matcher = new ProjectionTopicMatcher(ProjectionSubscription.ForTopics("a.b", "c.d"));
        matcher.Matches("a.b").ShouldBeTrue();
        matcher.Matches("a.bc").ShouldBeFalse();
    }

    [Fact(DisplayName = "Failure classes: Schema Registry unreachable / 5xx / timeout are transient; schema not found and bad bytes are not")]
    public void Schema_registry_failure_classes()
    {
        FailureClassifier.IsTransientSchemaRegistryFailure(new HttpRequestException("connection refused")).ShouldBeTrue();
        FailureClassifier.IsTransientSchemaRegistryFailure(new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway)).ShouldBeTrue();
        FailureClassifier.IsTransientSchemaRegistryFailure(new TaskCanceledException("timeout")).ShouldBeTrue();
        FailureClassifier.IsTransientSchemaRegistryFailure(new InvalidOperationException("wrapped", new SocketException())).ShouldBeTrue();
        FailureClassifier.IsTransientSchemaRegistryFailure(new SchemaRegistryException("unavailable", HttpStatusCode.ServiceUnavailable, 50003)).ShouldBeTrue();

        FailureClassifier.IsTransientSchemaRegistryFailure(new SchemaRegistryException("Schema not found", HttpStatusCode.NotFound, 40403)).ShouldBeFalse();
        FailureClassifier.IsTransientSchemaRegistryFailure(new InvalidDataException("Expecting data framing of length 5 bytes or more")).ShouldBeFalse();
        FailureClassifier.IsTransientSchemaRegistryFailure(new Avro.AvroException("bad")).ShouldBeFalse();
    }

    [Fact(DisplayName = "Catalog: two projections with the same name (same consumer group) are refused")]
    public void Duplicate_projection_names_are_refused()
    {
        var a = new TestProjection("dup", ProjectionSubscription.ForTopics("t"));
        var b = new TestProjection("dup", ProjectionSubscription.ForTopics("u"));
        Should.Throw<InvalidOperationException>(() => new ProjectionCatalog([a, b]));
    }

    [Theory(DisplayName = "CLI: --help / -h / help print usage and exit 0 without building a host")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    [InlineData("rebuild", "--help")]
    [InlineData("--Nexus:Kafka:BootstrapServers=x", "-h")]
    public async Task Help_exits_zero(params string[] args)
    {
        // The registration callback would run only if a host were built: it must not be.
        var exit = await ProjectionsCli.RunAsync(args, _ => throw new InvalidOperationException("no host on --help"), configure: null, TestContext.Current.CancellationToken);
        exit.ShouldBe(ProjectionsCli.ExitOk);
    }

    [Theory(DisplayName = "CLI: an unknown command or a rebuild without projection prints usage and exits 2")]
    [InlineData("bogus")]
    [InlineData("rebuild")]
    public async Task Unknown_command_exits_with_usage(string command)
    {
        var exit = await ProjectionsCli.RunAsync([command], _ => { }, configure: null, TestContext.Current.CancellationToken);
        exit.ShouldBe(ProjectionsCli.ExitUsage);
    }
}
