using System.Text.Json;

namespace Nexus.BuildingBlocks.Analytics.Audit;

/// <summary>
/// One event of an aggregate's audit trail, as stored in <c>nexus_analytics.event_log</c>
/// (<c>contracts/openapi/_shared/audit.yaml</c>, <c>AuditEvent</c>, minus
/// <c>actorDisplayName</c>, which the endpoint resolves against <c>org</c>).
/// </summary>
/// <param name="EventId">Event id (<c>nexus-event-id</c>).</param>
/// <param name="OccurredAt">When the event occurred (UTC, microsecond precision).</param>
/// <param name="EventType">Full event type, e.g. <c>org.legal-entity.updated</c>.</param>
/// <param name="AggregateVersion">Aggregate version the event was raised at.</param>
/// <param name="ActorUserId">User who caused the event.</param>
/// <param name="CorrelationId">Correlation id of the flow.</param>
/// <param name="Payload">The event payload as JSON; <see langword="null"/> for a <c>pii: true</c>
/// topic (stored as <c>payload_json = ''</c>, EV-R5).</param>
/// <param name="PreviousPayload">Payload of the immediately previous version of the same
/// aggregate visible to the caller; <see langword="null"/> for the first visible event, when this
/// event is PII, or when the previous one is PII.</param>
public sealed record AuditEventRecord(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string EventType,
    long AggregateVersion,
    Guid ActorUserId,
    Guid CorrelationId,
    JsonElement? Payload,
    JsonElement? PreviousPayload);
