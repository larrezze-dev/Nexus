using System.Text.Json;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Nexus.BuildingBlocks.Persistence.Outbox;

/// <summary>An event collected by the unit of work, with the aggregate version it was raised at.</summary>
public readonly record struct PendingDomainEvent(IDomainEvent Event, long AggregateVersion);

/// <summary>Envelope values shared by every event written in one unit of work.</summary>
public readonly record struct OutboxEnvelopeContext(Guid ActorUserId, Guid CorrelationId, Guid CausationId);

/// <summary>
/// Keys of the <c>outbox.message.headers</c> jsonb object: the envelope fields of
/// conventions-events.md §2 that have no dedicated column. Named after the A7 field names; the
/// relay (step 0.7) maps them, together with the column-backed fields, to the
/// <c>nexus-*</c> Kafka headers.
/// </summary>
public static class OutboxHeaders
{
    /// <summary>ISO-8601 UTC instant, microsecond precision, e.g. <c>2026-10-23T10:15:30.123456Z</c>.</summary>
    public const string OccurredAt = "occurred_at";

    public const string ActorUserId = "actor_user_id";

    public const string CorrelationId = "correlation_id";

    public const string CausationId = "causation_id";

    /// <summary>UUID, or JSON <c>null</c> when the event has no legal-entity scope.</summary>
    public const string LegalEntityId = "legal_entity_id";
}

/// <summary>
/// Writes domain events into <c>outbox.message</c> on the unit of work's own connection and
/// transaction (conventions-events.md §3). Every payload is serialized to Avro JSON encoding and
/// validated against its local <c>.avsc</c> first (EV-R1, EV-R2); any failure throws and the
/// unit of work rolls back.
/// </summary>
public interface IOutboxWriter
{
    Task WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PendingDomainEvent> events,
        OutboxEnvelopeContext context,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class OutboxWriter : IOutboxWriter
{
    // No RETURNING and no read-back: business roles only hold INSERT on outbox.message.
    // status/published_at/attempts/last_error take their column defaults.
    private const string InsertSql =
        "INSERT INTO outbox.message (id, topic, aggregate_type, aggregate_id, aggregate_version, " +
        "event_type, schema_version, headers, payload, created_at) " +
        "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)";

    private readonly IEventSchemaProvider _schemaProvider;
    private readonly IIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    public OutboxWriter(IEventSchemaProvider schemaProvider, IIdGenerator idGenerator, TimeProvider timeProvider)
    {
        _schemaProvider = schemaProvider;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
    }

    public async Task WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PendingDomainEvent> events,
        OutboxEnvelopeContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        // One instant for the whole unit of work, truncated to microseconds so that the
        // occurred_at header and the created_at column hold exactly the same value.
        var now = _timeProvider.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));

        // The relay orders by (created_at, id) (EV-R3). All rows of this unit of work share
        // created_at, and UUIDv7s minted in the same millisecond are not monotonic, so mint all
        // ids first and hand them out in ascending order: emission order == relay order.
        // Guid.CompareTo orders like PostgreSQL's uuid comparison (unsigned, big-endian
        // textual order), which a test pins down.
        var ids = new Guid[events.Count];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = _idGenerator.NewId();
        }

        Array.Sort(ids);

        // Serialize and validate everything before the first INSERT (EV-R2).
        var rows = new (Guid Id, PendingDomainEvent Pending, string Headers, string Payload)[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var pending = events[i];
            ArgumentNullException.ThrowIfNull(pending.Event);
            var schema = _schemaProvider.GetSchema(pending.Event);
            var payload = AvroPayloadSerializer.Serialize(pending.Event, schema);
            rows[i] = (ids[i], pending, BuildHeaders(now, context, pending.Event.LegalEntityId), payload);
        }

        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var row in rows)
        {
            var e = row.Pending.Event;
            var command = new NpgsqlBatchCommand(InsertSql);
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = row.Id, NpgsqlDbType = NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = e.Topic, NpgsqlDbType = NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = e.AggregateType, NpgsqlDbType = NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = e.AggregateId, NpgsqlDbType = NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = row.Pending.AggregateVersion, NpgsqlDbType = NpgsqlDbType.Bigint });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = e.EventType, NpgsqlDbType = NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = e.SchemaVersion, NpgsqlDbType = NpgsqlDbType.Integer });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = row.Headers, NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = row.Payload, NpgsqlDbType = NpgsqlDbType.Text }); // text, not jsonb: ADR-0013
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildHeaders(DateTimeOffset occurredAt, OutboxEnvelopeContext context, Guid? legalEntityId)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString(OutboxHeaders.OccurredAt, occurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            json.WriteString(OutboxHeaders.ActorUserId, context.ActorUserId);
            json.WriteString(OutboxHeaders.CorrelationId, context.CorrelationId);
            json.WriteString(OutboxHeaders.CausationId, context.CausationId);
            if (legalEntityId is { } le)
            {
                json.WriteString(OutboxHeaders.LegalEntityId, le);
            }
            else
            {
                json.WriteNull(OutboxHeaders.LegalEntityId);
            }

            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
