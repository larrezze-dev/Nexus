using Avro.Generic;
using Nexus.BuildingBlocks.Messaging;

namespace Nexus.Projections.Abstractions;

/// <summary>
/// One ClickHouse projection (conventions-events.md §8). The runtime (consumer, batching, offsets,
/// DLQ, rebuild) is generic; a projection only says what it reads and how one event becomes rows.
/// <para>
/// Contract fixed by the orchestrator (step 0.8 track S) so the runtime (S1) and the projections
/// (S2) can be written in parallel. Changing it means updating both sides.
/// </para>
/// </summary>
public interface IProjection
{
    /// <summary>Projection name, as used by <c>nexus-projections rebuild &lt;name&gt;</c>, e.g. <c>event_log</c>.
    /// The consumer group is <c>projections.&lt;name&gt;</c>.</summary>
    string Name { get; }

    /// <summary>Fully qualified target table, e.g. <c>nexus_analytics.event_log</c>. <c>rebuild</c> truncates it.</summary>
    string TableName { get; }

    /// <summary>Column names of <see cref="TableName"/>, in the order of every row returned by <see cref="Map"/>.</summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Which topics the projection consumes.</summary>
    ProjectionSubscription Subscription { get; }

    /// <summary>
    /// Maps one event to zero or more rows (each row's values in <see cref="ColumnNames"/> order, with
    /// ClickHouse.Client-compatible CLR types: <see cref="Guid"/>/<see cref="Nullable{Guid}"/> for UUID,
    /// <see cref="ulong"/> for UInt64, UTC <see cref="DateTime"/> for DateTime64, <see cref="bool"/>,
    /// <see cref="string"/>, <c>Dictionary&lt;string,string&gt;</c> for Map).
    /// <para>
    /// Pure and deterministic: no I/O, no clock (use <see cref="ProjectionRecord.IngestedAt"/>). Throw
    /// when the event cannot be mapped (missing field, wrong type...): the runtime retries and then
    /// sends the record to <c>{topic}.DLQ</c>. Return an empty list to skip an event on purpose.
    /// </para>
    /// </summary>
    IReadOnlyList<object?[]> Map(ProjectionRecord record);
}

/// <summary>
/// One consumed event: the Kafka topic, the parsed envelope (headers, conventions-events.md §2), the
/// Avro payload decoded as a <see cref="GenericRecord"/> against the Schema Registry (null for a
/// tombstone), and the ingestion instant the runtime assigns to the whole batch (<c>_ingested_at</c>,
/// UTC, millisecond precision).
/// </summary>
public sealed record ProjectionRecord(string Topic, EventEnvelope Envelope, GenericRecord? Payload, DateTime IngestedAt);

/// <summary>Topic selection of a projection: a librdkafka regex (must start with <c>^</c>) or an explicit list.</summary>
public sealed record ProjectionSubscription
{
    private ProjectionSubscription(string? pattern, IReadOnlyList<string>? topics)
    {
        Pattern = pattern;
        Topics = topics;
    }

    /// <summary>Regex subscription (librdkafka POSIX regex, no lookahead), or null.</summary>
    public string? Pattern { get; }

    /// <summary>Explicit topic list, or null.</summary>
    public IReadOnlyList<string>? Topics { get; }

    public static ProjectionSubscription ForPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (!pattern.StartsWith('^'))
            throw new ArgumentException("A Kafka regex subscription must start with '^'.", nameof(pattern));
        return new ProjectionSubscription(pattern, null);
    }

    public static ProjectionSubscription ForTopics(params string[] topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        if (topics.Length == 0 || topics.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty topic is required.", nameof(topics));
        return new ProjectionSubscription(null, topics);
    }
}
