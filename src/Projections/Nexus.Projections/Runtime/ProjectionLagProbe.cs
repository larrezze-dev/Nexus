using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Health;

namespace Nexus.Projections.Runtime;

/// <summary>
/// <see cref="IConsumerLagProbe"/> for projection groups. The building-block probe
/// (<see cref="KafkaConsumerLagProbe"/>) reads ONE topic; a projection may subscribe to a regex or to
/// several topics. This probe resolves the group's projection, expands its subscription against the
/// cluster metadata (<see cref="ProjectionTopicMatcher"/>, <c>*.DLQ</c> excluded) and sums
/// (high watermark − committed offset) over every partition of every matching topic, with four admin
/// requests in total (metadata, committed offsets, latest, earliest). A partition without a committed
/// offset counts from its earliest retained offset (<c>auto.offset.reset=earliest</c>). Does not join the
/// group. Registered before <c>AddNexusConsumerHealth</c>, which only adds its own probe if none exists.
/// </summary>
public sealed class ProjectionLagProbe : IConsumerLagProbe, IDisposable
{
    private readonly ProjectionCatalog _catalog;
    private readonly string _bootstrapServers;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private IAdminClient? _admin;

    public ProjectionLagProbe(
        ProjectionCatalog catalog,
        IOptions<NexusKafkaOptions> kafka,
        IOptions<ConsumerHealthOptions> health,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(kafka);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(time);
        _catalog = catalog;
        _bootstrapServers = kafka.Value.BootstrapServers;
        _timeout = health.Value.QueryTimeout;
        _time = time;
    }

    public async Task<ConsumerGroupLag> GetLagAsync(string groupId, string topic, CancellationToken ct)
    {
        var projection = _catalog.FindByGroupId(groupId)
            ?? throw new InvalidOperationException($"No projection uses consumer group '{groupId}'.");
        var started = _time.GetTimestamp();
        ct.ThrowIfCancellationRequested();
        var admin = Admin();
        var matcher = new ProjectionTopicMatcher(projection.Subscription);
        var metadata = admin.GetMetadata(Remaining(started, ct));
        var partitions = metadata.Topics
            .Where(t => !t.Error.IsError && matcher.Matches(t.Topic))
            .SelectMany(t => t.Partitions.Select(p => new TopicPartition(t.Topic, p.PartitionId)))
            .ToList();
        if (partitions.Count == 0)
        {
            return new ConsumerGroupLag(groupId, topic, 0);
        }

        var committedResult = await admin.ListConsumerGroupOffsetsAsync(
            [new ConsumerGroupTopicPartitions(groupId, partitions)],
            new ListConsumerGroupOffsetsOptions { RequestTimeout = Remaining(started, ct) }).ConfigureAwait(false);
        var committed = committedResult
            .SelectMany(r => r.Partitions)
            .GroupBy(p => p.TopicPartition)
            .ToDictionary(g => g.Key, g => g.First().Offset);

        var latest = await ListOffsetsAsync(admin, partitions, OffsetSpec.Latest(), Remaining(started, ct)).ConfigureAwait(false);
        var earliest = await ListOffsetsAsync(admin, partitions, OffsetSpec.Earliest(), Remaining(started, ct)).ConfigureAwait(false);

        long lag = 0;
        foreach (var tp in partitions)
        {
            var high = latest[tp];
            var position = committed.TryGetValue(tp, out var c) && c.Value >= 0 ? c.Value : earliest[tp];
            lag += Math.Max(0, high - position);
        }

        return new ConsumerGroupLag(groupId, topic, lag);
    }

    private TimeSpan Remaining(long started, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var left = _timeout - _time.GetElapsedTime(started);
        if (left <= TimeSpan.Zero)
        {
            throw new TimeoutException("The projection lag probe ran out of its time budget.");
        }

        return left;
    }

    private static async Task<Dictionary<TopicPartition, long>> ListOffsetsAsync(
        IAdminClient admin, List<TopicPartition> partitions, OffsetSpec spec, TimeSpan timeout)
    {
        var result = await admin.ListOffsetsAsync(
            partitions.Select(tp => new TopicPartitionOffsetSpec { TopicPartition = tp, OffsetSpec = spec }),
            new ListOffsetsOptions { RequestTimeout = timeout }).ConfigureAwait(false);
        return result.ResultInfos.ToDictionary(
            r => r.TopicPartitionOffsetError.TopicPartition,
            r => r.TopicPartitionOffsetError.Offset.Value);
    }

    private IAdminClient Admin()
    {
        lock (_gate)
        {
            return _admin ??= new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _admin?.Dispose();
            _admin = null;
        }
    }
}
