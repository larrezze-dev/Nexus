using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Nexus.BuildingBlocks.Messaging.Health;

/// <summary>Lag of one consumer group on one topic: Σ (high watermark − committed offset) over its partitions.</summary>
public sealed record ConsumerGroupLag(string GroupId, string Topic, long Lag);

/// <summary>Reads committed offsets and high watermarks from the cluster. Seam for the health check's tests.</summary>
public interface IConsumerLagProbe
{
    Task<ConsumerGroupLag> GetLagAsync(string groupId, string topic, CancellationToken ct);
}

/// <summary>
/// <see cref="IConsumerLagProbe"/> over one lazily-created <see cref="IAdminClient"/>: topic metadata →
/// <c>ListConsumerGroupOffsets</c> (committed) and <c>ListOffsets</c> (earliest/latest). Does not join
/// the group, so probing never triggers a rebalance of the real consumers.
/// <para>
/// The timeout given to the constructor bounds a WHOLE <see cref="GetLagAsync"/> call, not each admin
/// request: every request gets only what is left of it (the four requests are sequential and the
/// metadata one blocks its thread), and the token is checked between them.
/// </para>
/// </summary>
public sealed class KafkaConsumerLagProbe : IConsumerLagProbe, IDisposable
{
    private readonly string _bootstrapServers;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private IAdminClient? _admin;

    public KafkaConsumerLagProbe(string bootstrapServers, TimeSpan timeout, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        _bootstrapServers = bootstrapServers;
        _timeout = timeout;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<ConsumerGroupLag> GetLagAsync(string groupId, string topic, CancellationToken ct)
    {
        // The admin calls take a request timeout, not a token: bound them by one deadline.
        var started = _time.GetTimestamp();
        ct.ThrowIfCancellationRequested();
        var admin = Admin();
        var metadata = admin.GetMetadata(topic, Remaining(started, ct));
        var topicMetadata = metadata.Topics.SingleOrDefault(t => t.Topic == topic);
        if (topicMetadata is null || topicMetadata.Error.IsError)
        {
            throw new KafkaException(topicMetadata?.Error ?? new Error(ErrorCode.UnknownTopicOrPart));
        }

        var partitions = topicMetadata.Partitions.Select(p => new TopicPartition(topic, p.PartitionId)).ToList();
        if (partitions.Count == 0)
        {
            return new ConsumerGroupLag(groupId, topic, 0);
        }

        var committedResult = await admin.ListConsumerGroupOffsetsAsync(
            [new ConsumerGroupTopicPartitions(groupId, partitions)],
            new ListConsumerGroupOffsetsOptions { RequestTimeout = Remaining(started, ct) }).ConfigureAwait(false);
        var committed = committedResult
            .SelectMany(r => r.Partitions)
            .ToDictionary(p => p.TopicPartition, p => p.Offset);

        var latest = await ListOffsetsAsync(admin, partitions, OffsetSpec.Latest(), Remaining(started, ct)).ConfigureAwait(false);
        var earliest = await ListOffsetsAsync(admin, partitions, OffsetSpec.Earliest(), Remaining(started, ct)).ConfigureAwait(false);

        long lag = 0;
        foreach (var tp in partitions)
        {
            var high = latest[tp];
            // No commit yet: the group starts at the earliest retained offset (auto.offset.reset=earliest).
            var position = committed.TryGetValue(tp, out var c) && c.Value >= 0 ? c.Value : earliest[tp];
            lag += Math.Max(0, high - position);
        }

        return new ConsumerGroupLag(groupId, topic, lag);
    }

    /// <summary>What is left of the call's budget since <paramref name="started"/>; throws when nothing is.</summary>
    private TimeSpan Remaining(long started, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var left = _timeout - _time.GetElapsedTime(started);
        if (left <= TimeSpan.Zero)
        {
            throw new TimeoutException("The consumer lag probe ran out of its time budget.");
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
