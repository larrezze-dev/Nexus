using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.Projections.Abstractions;
using Nexus.Projections.Migrations;
using Nexus.Projections.Projections;
using Nexus.Projections.Runtime;

namespace Nexus.Projections.Tests.Runtime.Infrastructure;

/// <summary>Kafka + Schema Registry + ClickHouse shared by every runtime test (one collection, run serially).</summary>
public sealed class RuntimeEnvironment : IAsyncLifetime
{
    public KafkaFixture Kafka { get; } = new();

    public ClickHouseFixture ClickHouse { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Kafka.InitializeAsync().AsTask(), ClickHouse.InitializeAsync().AsTask());
        // The runtime's own applier creates nexus_analytics (base migration) once for the collection.
        await using var harness = RuntimeHarness.Create(this, []);
        await harness.Migrator.ApplyAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await Kafka.DisposeAsync();
        await ClickHouse.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RuntimeTestGroup : ICollectionFixture<RuntimeEnvironment>
{
    public const string Name = "projections-runtime";
}

/// <summary>
/// The real runtime wiring (<see cref="ProjectionsServiceCollectionExtensions.AddNexusProjectionsRuntime"/>)
/// over the test environment, with fast retries, plus a metrics recorder.
/// </summary>
internal sealed class RuntimeHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly MeterListener _listener = new();
    private readonly List<RunningConsumer> _running = [];

    private RuntimeHarness(ServiceProvider provider)
    {
        _provider = provider;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ProjectionMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
        _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
        _listener.SetMeasurementEventCallback<int>((i, v, tags, _) => Record(i, v, tags));
        _listener.Start();
    }

    /// <summary>(instrument, projection tag, kind tag, value) of every measurement of the Nexus.Projections meter.</summary>
    public ConcurrentQueue<(string Instrument, string? Projection, string? Kind, double Value)> Measurements { get; } = new();

    public IServiceProvider Services => _provider;

    public ProjectionRuntime Runtime => _provider.GetRequiredService<ProjectionRuntime>();

    public ConsumerHealthRegistry Health => _provider.GetRequiredService<ConsumerHealthRegistry>();

    public ClickHouseMigrator Migrator => _provider.GetRequiredService<ClickHouseMigrator>();

    public ProjectionRebuilder Rebuilder => _provider.GetRequiredService<ProjectionRebuilder>();

    public ProjectionCatalog Catalog => _provider.GetRequiredService<ProjectionCatalog>();

    public static Dictionary<string, string?> BaseConfiguration(RuntimeEnvironment env) => new()
    {
        [$"{NexusKafkaOptions.SectionName}:BootstrapServers"] = env.Kafka.BootstrapServers,
        [$"{NexusKafkaOptions.SectionName}:SchemaRegistryUrl"] = env.Kafka.SchemaRegistryUrl,
        [$"{NexusKafkaOptions.SectionName}:DlqMessageTimeout"] = "00:00:05",
        [$"{ClickHouseOptions.SectionName}:ConnectionString"] = env.ClickHouse.ConnectionString,
        [$"{ProjectionOptions.SectionName}:FlushInterval"] = "00:00:00.300",
        [$"{ProjectionOptions.SectionName}:TransientInitialBackoff"] = "00:00:00.300",
        [$"{ProjectionOptions.SectionName}:TransientMaxBackoff"] = "00:00:01",
        [$"{ProjectionOptions.SectionName}:TopicMetadataRefreshInterval"] = "00:00:02",
        [$"{ProjectionOptions.SectionName}:SchemaRegistryRequestTimeout"] = "00:00:02",
    };

    /// <param name="realDefinitions">Also register the production projections (<c>AddNexusProjectionDefinitions</c>).</param>
    public static RuntimeHarness Create(
        RuntimeEnvironment env,
        IEnumerable<IProjection> projections,
        Action<Dictionary<string, string?>>? configure = null,
        bool realDefinitions = false)
    {
        var settings = BaseConfiguration(env);
        configure?.Invoke(settings);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b =>
        {
            if (Environment.GetEnvironmentVariable("NEXUS_TEST_LOGS") == "1")
            {
                b.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug);
            }
        });
        // Fast retries (TryAdd in the runtime keeps this one): 4 attempts, 50 ms apart.
        services.AddSingleton(new RetryOptions
        {
            Delays = [TimeSpan.FromMilliseconds(50)],
            DlqPublishInitialBackoff = TimeSpan.FromMilliseconds(200),
            DlqPublishMaxBackoff = TimeSpan.FromSeconds(1),
        });
        services.AddNexusProjectionsRuntime(configuration);
        foreach (var projection in projections)
        {
            services.AddSingleton(projection);
        }

        if (realDefinitions)
        {
            services.AddNexusProjectionDefinitions();
        }

        return new RuntimeHarness(services.BuildServiceProvider());
    }

    /// <summary>Starts the worker loop of <paramref name="projection"/> (as <see cref="ProjectionWorker"/> does).</summary>
    public RunningConsumer Start(IProjection projection)
    {
        var consumer = new ProjectionConsumer(projection, Runtime);
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => consumer.RunAsync(cts.Token));
        var running = new RunningConsumer(consumer, task, cts);
        _running.Add(running);
        return running;
    }

    public ConsumerLoopState State(string groupId) =>
        Health.Snapshot().Single(s => s.GroupId == groupId).State;

    public IEnumerable<double> Values(string instrument, string projection) =>
        Measurements.Where(m => m.Instrument == instrument && m.Projection == projection).Select(m => m.Value);

    public double Sum(string instrument, string projection, string? kind = null) =>
        Measurements.Where(m => m.Instrument == instrument && m.Projection == projection && (kind is null || m.Kind == kind)).Sum(m => m.Value);

    public async ValueTask DisposeAsync()
    {
        foreach (var running in _running)
        {
            await running.StopAsync();
        }

        _listener.Dispose();
        await _provider.DisposeAsync();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? projection = null;
        string? kind = null;
        foreach (var tag in tags)
        {
            if (tag.Key == "projection")
            {
                projection = tag.Value as string;
            }
            else if (tag.Key == "kind")
            {
                kind = tag.Value as string;
            }
        }

        Measurements.Enqueue((instrument.Name, projection, kind, value));
    }
}

internal sealed class RunningConsumer(ProjectionConsumer consumer, Task task, CancellationTokenSource cts)
{
    public ProjectionConsumer Consumer { get; } = consumer;

    public Task Task { get; } = task;

    public async Task StopAsync()
    {
        if (!cts.IsCancellationRequested)
        {
            await cts.CancelAsync();
        }

        try
        {
            await Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (OperationCanceledException)
        {
        }

        cts.Dispose();
    }
}

internal static class Eventually
{
    /// <summary>Polls <paramref name="condition"/> every 100 ms until it holds or <paramref name="timeout"/> elapses; returns whether it held.</summary>
    public static async Task<bool> Until(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return await condition();
    }

    public static Task<bool> Until(Func<bool> condition, TimeSpan timeout) => Until(() => Task.FromResult(condition()), timeout);
}
