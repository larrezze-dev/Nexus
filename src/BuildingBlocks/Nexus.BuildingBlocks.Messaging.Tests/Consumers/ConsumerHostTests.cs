using ch.nussli.nexus.org;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Shouldly;
using static Nexus.BuildingBlocks.Messaging.Tests.Consumers.ConsumerTestKit;
using static Nexus.BuildingBlocks.Messaging.Tests.Inbox.InboxTestHost;

namespace Nexus.BuildingBlocks.Messaging.Tests.Consumers;

/// <summary>A consumer as a service writes it: one constructor dependency, found by assembly scan.</summary>
internal sealed class SampleLegalEntityConsumer(NexusConsumerRuntime runtime)
    : NexusConsumer<LegalEntityCreated>(
        runtime.CreateOptions("operations", "sample-on-legal-entity-created", "nexus.org.legal-entity.created"), runtime)
{
    public ConsumerOptions ExposedOptions => Options;

    protected override Task HandleAsync(LegalEntityCreated message, EventEnvelope envelope, IServiceProvider services, CancellationToken ct) =>
        Task.CompletedTask;
}

/// <summary>Abstract intermediate bases are not registered.</summary>
internal abstract class AbstractLegalEntityConsumer(ConsumerOptions options, NexusConsumerRuntime runtime)
    : NexusConsumer<LegalEntityCreated>(options, runtime);

/// <summary>ConsumerHost: registration by assembly scan and health (step 0.7 track S).</summary>
[Collection(KafkaPostgresFixtures.Name)]
public sealed class ConsumerHostTests(KafkaFixture kafka, PostgresFixture db)
{
    private ServiceCollection Services(bool withPersistence = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Nexus:Kafka:BootstrapServers"] = kafka.BootstrapServers,
            ["Nexus:Kafka:SchemaRegistryUrl"] = kafka.SchemaRegistryUrl,
            ["Nexus:Consumers:Health:LagDegradedThreshold"] = "2",
        }).Build());
        services.AddNexusApplication();
        if (withPersistence)
        {
            services.AddSingleton(db.GetServiceDataSource(ServiceRole));
            services.AddSingleton<IEventSchemaProvider>(new FileSystemEventSchemaProvider(RepoRoot.Find("contracts/events/_test")));
            services.AddNexusPersistence();
        }

        return services;
    }

    [Fact(DisplayName = "ConsumerHost: AddNexusConsumers registers every concrete NexusConsumer of the assembly as a hosted service with the shared runtime")]
    public async Task AddNexusConsumers_RegistersConcreteConsumers()
    {
        var services = Services();
        services.AddNexusConsumers(typeof(SampleLegalEntityConsumer).Assembly);
        services.AddNexusConsumers(typeof(SampleLegalEntityConsumer).Assembly); // idempotent

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).ToList();
        hosted.ShouldContain(typeof(SampleLegalEntityConsumer));
        hosted.ShouldContain(typeof(TestLegalEntityConsumer)); // also concrete; never resolved by these tests
        hosted.ShouldNotContain(typeof(AbstractLegalEntityConsumer));
        hosted.Count(t => t == typeof(SampleLegalEntityConsumer)).ShouldBe(1);

        await using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<NexusConsumerRuntime>();
        runtime.Kafka.BootstrapServers.ShouldBe(kafka.BootstrapServers);
        runtime.RetryOptions.MaxAttempts.ShouldBe(4); // 1 attempt + 3 retries
        runtime.Kafka.DlqMessageTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        runtime.DlqPublisher.ShouldBeOfType<KafkaDlqPublisher>();
        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(RoundedSystemTimeProvider.Instance);

        var consumer = ActivatorUtilities.CreateInstance<SampleLegalEntityConsumer>(provider);
        consumer.ExposedOptions.GroupId.ShouldBe("operations.sample-on-legal-entity-created");
        consumer.ExposedOptions.Identity.ShouldBe(ActingIdentity.System);
        consumer.ExposedOptions.SchemaRegistryUrl.ShouldBe(kafka.SchemaRegistryUrl);
        consumer.CreateConsumerConfig().MaxPollIntervalMs.ShouldBe(300_000, "max.poll.interval.ms is explicit (time budget in NexusKafkaOptions)");

        // AddNexusInbox ran (ConsumerIdentity + the ICurrentUser decorator), but the inbox processor is
        // never resolvable from DI (ADR-0017): only NexusConsumer instantiates it.
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ConsumerIdentity>().UserId.ShouldBeNull();
        scope.ServiceProvider.GetService<IInboxProcessor>().ShouldBeNull();
        scope.ServiceProvider.GetService<InboxProcessor>().ShouldBeNull();
        services.ShouldNotContain(d => d.ServiceType == typeof(IInboxProcessor) || d.ServiceType == typeof(InboxProcessor));
    }

    [Fact(DisplayName = "ConsumerHost: AddNexusConsumers before AddNexusPersistence fails fast")]
    public void AddNexusConsumers_WithoutPersistence_Throws()
    {
        var services = Services(withPersistence: false);
        Should.Throw<InvalidOperationException>(() => services.AddNexusConsumers(typeof(SampleLegalEntityConsumer).Assembly))
            .Message.ShouldContain("AddNexusPersistence");
    }

    [Fact(DisplayName = "ConsumerHost health: /health reports the real lag per group read from Kafka (committed offset vs high watermark)")]
    public async Task Health_ReportsRealLagPerGroup()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        for (var i = 0; i < 4; i++)
        {
            await ProduceRawAsync(kafka, topic, $"k{i}", [1, 2, 3], new Headers());
        }

        var services = Services();
        services.AddNexusConsumers();
        services.AddHealthChecks().AddNexusConsumerHealth();
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerHealthRegistry>();
        var group = $"operations.lag-{Guid.CreateVersion7():N}";
        registry.Register(group, topic);
        registry.Report(group, ConsumerLoopState.Running);

        var health = provider.GetRequiredService<HealthCheckService>();
        var report = await health.CheckHealthAsync(TestContext.Current.CancellationToken);
        var entry = report.Entries["kafka-consumers"];
        entry.Data[$"{group}.lag"].ShouldBe(4L); // nothing committed yet
        entry.Status.ShouldBe(HealthStatus.Degraded); // threshold 2

        // Commit 3 of the 4 records for the group: lag 1. A fresh probe (the check caches ≥ 5 s).
        using (var committer = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig
               {
                   BootstrapServers = kafka.BootstrapServers,
                   GroupId = group,
                   EnableAutoCommit = false,
               }).Build())
        {
            committer.Commit([new TopicPartitionOffset(topic, 0, 3)]);
        }

        using var probe = new KafkaConsumerLagProbe(kafka.BootstrapServers, TimeSpan.FromSeconds(10));
        (await probe.GetLagAsync(group, topic, TestContext.Current.CancellationToken)).Lag.ShouldBe(1);
        provider.GetRequiredService<IOptions<ConsumerHealthOptions>>().Value.LagDegradedThreshold.ShouldBe(2);
    }
}
