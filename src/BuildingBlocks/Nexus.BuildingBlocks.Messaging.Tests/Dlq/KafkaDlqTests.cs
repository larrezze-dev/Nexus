using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Tests.Consumers;
using Shouldly;
using static Nexus.BuildingBlocks.Messaging.Tests.Consumers.ConsumerTestKit;

namespace Nexus.BuildingBlocks.Messaging.Tests.Dlq;

/// <summary>DLQ producer timing and <c>make dlq-replay</c> edge cases, against a real Kafka.</summary>
[Collection(KafkaPostgresFixtures.Name)]
public sealed class KafkaDlqTests(KafkaFixture kafka)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "KafkaDlqPublisher: message.timeout.ms is explicit (30 s by default, configurable), not librdkafka's 5 min")]
    public void MessageTimeout_IsExplicit()
    {
        KafkaDlqPublisher.CreateConfig("localhost:9092", KafkaDlqPublisher.DefaultMessageTimeout).MessageTimeoutMs.ShouldBe(30_000);
        KafkaDlqPublisher.CreateConfig("localhost:9092", TimeSpan.FromSeconds(45)).MessageTimeoutMs.ShouldBe(45_000);
        new NexusKafkaOptions().DlqMessageTimeout.ShouldBe(KafkaDlqPublisher.DefaultMessageTimeout);
        new NexusKafkaOptions().MaxPollInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact(DisplayName = "KafkaDlqPublisher: a publish to an unavailable DLQ fails within its message timeout instead of hanging")]
    public async Task PublishToMissingDlq_FailsWithinMessageTimeout()
    {
        using var publisher = new KafkaDlqPublisher(kafka.BootstrapServers, TimeProvider.System, TimeSpan.FromSeconds(2));
        var topic = NewTopic(); // neither the topic nor its .DLQ exists; auto-create is off

        var watch = Stopwatch.StartNew();
        await Should.ThrowAsync<KafkaException>(() => publisher
            .PublishAsync(topic, "k", [1, 2, 3], new Headers(), new InvalidOperationException("boom"), "operations.x", Ct)
            .WaitAsync(TimeSpan.FromSeconds(20), Ct));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "make dlq-replay: a DLQ whose last offset holds no record (transaction marker) is replayed to the end instead of blocking")]
    public async Task Replay_LastOffsetWithoutRecord_DoesNotBlock()
    {
        var topic = NewTopic();
        var dlqTopic = DlqHeaders.TopicFor(topic);
        await kafka.CreateTopicWithDlqAsync(topic);

        // A transactional write: record at offset 0, commit marker at offset 1, high watermark 2.
        using (var producer = new ProducerBuilder<string?, byte[]?>(new ProducerConfig
               {
                   BootstrapServers = kafka.BootstrapServers,
                   TransactionalId = $"dlq-test-{Guid.CreateVersion7():N}",
               }).Build())
        {
            producer.InitTransactions(TimeSpan.FromSeconds(30));
            producer.BeginTransaction();
            await producer.ProduceAsync(dlqTopic, new Message<string?, byte[]?> { Key = "k", Value = Encoding.UTF8.GetBytes("v"), Headers = [] }, Ct);
            producer.CommitTransaction(TimeSpan.FromSeconds(30));
        }

        var replayer = new DlqReplayer(kafka.BootstrapServers);
        replayer.Plan(dlqTopic).Pending.ShouldBe(2, "Pending is an upper bound: it counts the marker's offset");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        (await replayer.ReplayAsync(dlqTopic, timeout.Token)).ShouldBe(1);

        var replayed = kafka.ReadAll(topic, expected: 2, TimeSpan.FromSeconds(3));
        replayed.Count.ShouldBe(1);
        replayed[0].Message.Key.ShouldBe("k");
        replayer.Plan(dlqTopic).Pending.ShouldBe(0, "the marker's offset is recorded as progress");
        (await replayer.ReplayAsync(dlqTopic, timeout.Token)).ShouldBe(0);
    }
}
