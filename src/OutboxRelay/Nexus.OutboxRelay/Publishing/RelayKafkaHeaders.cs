using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Nexus.BuildingBlocks.Persistence.Outbox;

namespace Nexus.OutboxRelay.Publishing;

/// <summary>
/// The Kafka headers of the event envelope (conventions-events.md §2). Column-backed fields come
/// from the row; the rest from the <c>headers</c> jsonb, keyed by <see cref="OutboxHeaders"/>
/// (written by <c>OutboxWriter</c>). Values are UTF-8 text; <c>nexus-legal-entity-id</c> is an
/// empty value when the event has no legal-entity scope (JSON <c>null</c>).
/// </summary>
public static class RelayKafkaHeaders
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

    /// <summary>Builds the headers, in the order of conventions-events.md §2.</summary>
    /// <exception cref="PayloadEncodingException">The <c>headers</c> jsonb lacks a required key (never transient).</exception>
    public static Headers Build(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string occurredAt, actorUserId, correlationId, causationId, legalEntityId;
        try
        {
            using var json = JsonDocument.Parse(message.HeadersJson);
            var root = json.RootElement;
            occurredAt = Required(root, OutboxHeaders.OccurredAt);
            actorUserId = Required(root, OutboxHeaders.ActorUserId);
            correlationId = Required(root, OutboxHeaders.CorrelationId);
            causationId = Required(root, OutboxHeaders.CausationId);
            legalEntityId = root.TryGetProperty(OutboxHeaders.LegalEntityId, out var le) && le.ValueKind == JsonValueKind.String
                ? le.GetString()!
                : le.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    ? string.Empty
                    : throw new PayloadEncodingException($"headers.{OutboxHeaders.LegalEntityId} must be a string or null.", isTransient: false);
        }
        catch (JsonException ex)
        {
            throw new PayloadEncodingException("outbox.message.headers is not valid JSON.", isTransient: false, ex);
        }

        return new Headers
        {
            { EventId, Utf8(message.Id.ToString("D")) },
            { EventType, Utf8(message.EventType) },
            { AggregateType, Utf8(message.AggregateType) },
            { AggregateId, Utf8(message.AggregateId.ToString("D")) },
            { AggregateVersion, Utf8(message.AggregateVersion.ToString(CultureInfo.InvariantCulture)) },
            { OccurredAt, Utf8(occurredAt) },
            { ActorUserId, Utf8(actorUserId) },
            { SchemaVersion, Utf8(message.SchemaVersion.ToString(CultureInfo.InvariantCulture)) },
            { CorrelationId, Utf8(correlationId) },
            { CausationId, Utf8(causationId) },
            { LegalEntityId, Utf8(legalEntityId) },
        };
    }

    private static string Required(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new PayloadEncodingException($"headers.{key} is missing or not a string.", isTransient: false);

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
