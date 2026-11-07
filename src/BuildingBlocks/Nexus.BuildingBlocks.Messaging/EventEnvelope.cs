using System.Globalization;
using System.Text;
using Confluent.Kafka;

namespace Nexus.BuildingBlocks.Messaging;

/// <summary>Header names of the event envelope (conventions-events.md §2).</summary>
public static class EventHeaderNames
{
    public const string EventId = "nexus-event-id";
    public const string EventType = "nexus-event-type";
    public const string AggregateType = "nexus-aggregate-type";
    public const string AggregateId = "nexus-aggregate-id";
    public const string AggregateVersion = "nexus-aggregate-version";
    public const string OccurredAt = "nexus-occurred-at";
    public const string ActorUserId = "nexus-actor-user-id";
    public const string SchemaVersion = "nexus-schema-version";
    public const string CorrelationId = "nexus-correlation-id";
    public const string CausationId = "nexus-causation-id";
    public const string LegalEntityId = "nexus-legal-entity-id";
}

public sealed class InvalidEventEnvelopeException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Event metadata carried in Kafka headers.</summary>
public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    string AggregateType,
    Guid AggregateId,
    long AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    int SchemaVersion,
    Guid CorrelationId,
    Guid CausationId,
    Guid? LegalEntityId)
{
    /// <summary>
    /// Parses the envelope. Mandatory: all headers except actor-user-id and legal-entity-id,
    /// which may be absent or empty (null).
    /// </summary>
    public static EventEnvelope FromHeaders(Headers headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return new EventEnvelope(
            ParseGuid(Required(headers, EventHeaderNames.EventId), EventHeaderNames.EventId),
            Required(headers, EventHeaderNames.EventType),
            Required(headers, EventHeaderNames.AggregateType),
            ParseGuid(Required(headers, EventHeaderNames.AggregateId), EventHeaderNames.AggregateId),
            ParseLong(Required(headers, EventHeaderNames.AggregateVersion), EventHeaderNames.AggregateVersion),
            ParseDate(Required(headers, EventHeaderNames.OccurredAt), EventHeaderNames.OccurredAt),
            OptionalGuid(headers, EventHeaderNames.ActorUserId),
            (int)ParseLong(Required(headers, EventHeaderNames.SchemaVersion), EventHeaderNames.SchemaVersion),
            ParseGuid(Required(headers, EventHeaderNames.CorrelationId), EventHeaderNames.CorrelationId),
            ParseGuid(Required(headers, EventHeaderNames.CausationId), EventHeaderNames.CausationId),
            OptionalGuid(headers, EventHeaderNames.LegalEntityId));
    }

    private static string? Read(Headers headers, string name) =>
        headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private static string Required(Headers headers, string name)
    {
        var value = Read(headers, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidEventEnvelopeException($"Missing mandatory event header '{name}'.");
        return value;
    }

    private static Guid? OptionalGuid(Headers headers, string name)
    {
        var value = Read(headers, name);
        return string.IsNullOrWhiteSpace(value) ? null : ParseGuid(value, name);
    }

    private static Guid ParseGuid(string value, string name) =>
        Guid.TryParse(value, out var g)
            ? g
            : throw new InvalidEventEnvelopeException($"Event header '{name}' is not a valid UUID: '{value}'.");

    private static long ParseLong(string value, string name) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new InvalidEventEnvelopeException($"Event header '{name}' is not a valid integer: '{value}'.");

    private static DateTimeOffset ParseDate(string value, string name) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d
            : throw new InvalidEventEnvelopeException($"Event header '{name}' is not a valid ISO-8601 date: '{value}'.");
}
