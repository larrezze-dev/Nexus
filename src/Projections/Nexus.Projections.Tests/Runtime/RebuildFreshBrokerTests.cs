using Nexus.BuildingBlocks.Messaging;
using Nexus.Projections.Abstractions;
using Nexus.Projections.Tests.Runtime.Infrastructure;
using Shouldly;
using Testcontainers.Kafka;

namespace Nexus.Projections.Tests.Runtime;

/// <summary>
/// N1: right after a broker starts, its group coordinator is still loading (<c>describe</c> / <c>delete
/// group</c> fail with per-group transient errors under a top-level <c>Local_Partial</c>). The rebuild must
/// retry those, not exit 1.
/// </summary>
[Collection(RuntimeTestGroup.Name)]
public sealed class RebuildFreshBrokerTests(RuntimeEnvironment env)
{
    [Fact(DisplayName = "rebuild (N1): against a just-started broker the coordinator errors are retried and the rebuild succeeds")]
    public async Task Rebuild_against_fresh_broker_succeeds()
    {
        await using var kafka = new KafkaBuilder("apache/kafka:4.3.2")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
            .Build();
        await kafka.StartAsync(TestContext.Current.CancellationToken);

        // Immediately: no warm-up request, so the first admin calls hit a coordinator that may still be loading.
        var name = TestProjection.UniqueName("fresh");
        var p = new TestProjection(name, ProjectionSubscription.ForTopics($"test.{name}"));
        await env.ClickHouse.ExecuteAsync(p.CreateTableSql);
        await using var h = RuntimeHarness.Create(env, [p], c => c[$"{NexusKafkaOptions.SectionName}:BootstrapServers"] = kafka.GetBootstrapAddress());

        var summary = await h.Rebuilder.RunAsync(p.Name, TestContext.Current.CancellationToken);

        summary.Partitions.ShouldBe(0, "the topic does not exist: nothing to replay");
        summary.TotalRows.ShouldBe(0);
    }
}
