using Confluent.Kafka;

namespace Nexus.BuildingBlocks.Messaging.Dlq;

/// <summary>
/// What a replay of <see cref="DlqTopic"/> would re-inject into <see cref="TargetTopic"/>. <see cref="Pending"/>
/// is an upper bound (high watermark − progress): offsets without a deliverable record (transaction
/// markers, compacted or aborted records) count here but are never re-injected.
/// </summary>
public sealed record DlqReplayPlan(string DlqTopic, string TargetTopic, long Pending);

/// <summary>
/// Re-injects the records of <c>{topic}.DLQ</c> into <c>{topic}</c> (<c>make dlq-replay</c>), byte for byte:
/// same key (so the same partition as the original), same value, original headers WITHOUT the
/// <c>nexus-dlq-*</c> ones. Re-injection is safe for consumers that already processed an event (the inbox
/// skips its <c>event_id</c>).
/// <para>
/// Progress is kept as the committed offset of the consumer group <see cref="GroupIdFor"/>, committed after
/// each record is acknowledged by the broker: a second replay only re-injects what reached the DLQ since
/// (at-least-once: a crash between produce and commit re-injects that one record again, harmless).
/// A replay stops at the high watermarks read when IT starts (<see cref="ReplayAsync"/>, not
/// <see cref="Plan"/>), so records that fail again and land back in the DLQ during the replay are not
/// looped over. Records that reached the DLQ between the operator's confirmation of the plan and the start
/// of the replay ARE re-injected too: the count actually re-injected is what <see cref="ReplayAsync"/>
/// returns, and it may differ from <see cref="DlqReplayPlan.Pending"/> in either direction.
/// </para>
/// <para>
/// A partition is done when the record at <c>end − 1</c> was handled OR the end of the partition is
/// reached (<c>enable.partition.eof</c>): the offset <c>end − 1</c> may hold no deliverable record (e.g. a
/// transaction commit marker), and waiting for it would block forever.
/// </para>
/// </summary>
public sealed class DlqReplayer
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] DlqHeaderNames = [DlqHeaders.Error, DlqHeaders.Consumer, DlqHeaders.FailedAt];

    private readonly string _bootstrapServers;

    public DlqReplayer(string bootstrapServers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        _bootstrapServers = bootstrapServers;
    }

    public static string GroupIdFor(string dlqTopic) => $"nexus.dlq-replay.{dlqTopic}";

    /// <summary>The original topic of <paramref name="dlqTopic"/> (<c>x.DLQ</c> → <c>x</c>).</summary>
    public static string OriginalTopic(string dlqTopic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dlqTopic);
        const string suffix = ".DLQ";
        if (!dlqTopic.EndsWith(suffix, StringComparison.Ordinal) || dlqTopic.Length == suffix.Length)
        {
            throw new ArgumentException($"'{dlqTopic}' is not a DLQ topic (expected '<topic>{suffix}').", nameof(dlqTopic));
        }

        return dlqTopic[..^suffix.Length];
    }

    public DlqReplayPlan Plan(string dlqTopic)
    {
        var target = OriginalTopic(dlqTopic);
        using var consumer = BuildConsumer(dlqTopic);
        var ranges = Ranges(consumer, dlqTopic);
        return new DlqReplayPlan(dlqTopic, target, ranges.Sum(r => r.End - r.Start));
    }

    /// <summary>Re-injects every pending record; returns how many were re-injected.</summary>
    public async Task<long> ReplayAsync(string dlqTopic, CancellationToken ct)
    {
        var target = OriginalTopic(dlqTopic);
        using var consumer = BuildConsumer(dlqTopic);
        var ranges = Ranges(consumer, dlqTopic).Where(r => r.End > r.Start).ToList();
        if (ranges.Count == 0)
        {
            return 0;
        }

        using var producer = new ProducerBuilder<byte[]?, byte[]?>(new ProducerConfig
        {
            BootstrapServers = _bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();

        consumer.Assign(ranges.Select(r => new TopicPartitionOffset(r.Partition, r.Start)));
        var remaining = ranges.ToDictionary(r => r.Partition, r => r.End);
        long replayed = 0;

        while (remaining.Count > 0)
        {
            var record = consumer.Consume(ct);
            if (record is null || !remaining.TryGetValue(record.TopicPartition, out var end))
            {
                continue;
            }

            if (record.IsPartitionEOF)
            {
                // Nothing more to read right now: every deliverable record below the starting high
                // watermark was seen (a gap at end - 1 cannot block the replay). Records at or past
                // `end` remove the partition before an EOF can show up, so everything below the EOF
                // position was handled: record it as progress (the gap is not "pending" any more).
                if (record.Offset.Value > 0)
                {
                    consumer.Commit([new TopicPartitionOffset(record.TopicPartition, record.Offset)]);
                }

                remaining.Remove(record.TopicPartition);
                consumer.Pause([record.TopicPartition]);
                continue;
            }

            if (record.Offset.Value < end)
            {
                await producer.ProduceAsync(target, new Message<byte[]?, byte[]?>
                {
                    Key = record.Message.Key,
                    Value = record.Message.Value,
                    Headers = StripDlqHeaders(record.Message.Headers),
                }, ct).ConfigureAwait(false);
                consumer.Commit([new TopicPartitionOffset(record.TopicPartition, record.Offset + 1)]);
                replayed++;
            }

            if (record.Offset.Value + 1 >= end)
            {
                remaining.Remove(record.TopicPartition);
                consumer.Pause([record.TopicPartition]);
            }
        }

        return replayed;
    }

    internal static Headers StripDlqHeaders(Headers? headers)
    {
        var result = new Headers();
        if (headers is null)
        {
            return result;
        }

        foreach (var header in headers)
        {
            if (!DlqHeaderNames.Contains(header.Key, StringComparer.Ordinal))
            {
                result.Add(header.Key, header.GetValueBytes());
            }
        }

        return result;
    }

    // Raw bytes for key and value: the key is forwarded untouched so it hashes to the same partition.
    private IConsumer<byte[]?, byte[]?> BuildConsumer(string dlqTopic) =>
        new ConsumerBuilder<byte[]?, byte[]?>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = GroupIdFor(dlqTopic),
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = true,
        }).Build();

    private static List<(TopicPartition Partition, long Start, long End)> Ranges(IConsumer<byte[]?, byte[]?> consumer, string dlqTopic)
    {
        using var admin = new DependentAdminClientBuilder(consumer.Handle).Build();
        var topic = admin.GetMetadata(dlqTopic, MetadataTimeout).Topics.SingleOrDefault(t => t.Topic == dlqTopic);
        if (topic is null || topic.Error.IsError)
        {
            throw new InvalidOperationException($"Topic '{dlqTopic}' does not exist ({topic?.Error.Reason ?? "no metadata"}).");
        }

        var partitions = topic.Partitions.Select(p => new TopicPartition(dlqTopic, p.PartitionId)).ToList();
        var committed = consumer.Committed(partitions, MetadataTimeout).ToDictionary(c => c.TopicPartition, c => c.Offset);

        return partitions.Select(tp =>
        {
            var watermarks = consumer.QueryWatermarkOffsets(tp, MetadataTimeout);
            var start = committed.TryGetValue(tp, out var c) && c.Value >= 0 ? Math.Max(c.Value, watermarks.Low.Value) : watermarks.Low.Value;
            return (tp, start, watermarks.High.Value);
        }).ToList();
    }
}
