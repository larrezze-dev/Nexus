using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.BuildingBlocks.Web.Audit;

/// <summary>
/// One event of the audit trail (<c>contracts/openapi/_shared/audit.yaml</c>, <c>AuditEvent</c>).
/// </summary>
public sealed record AuditEvent(
    Guid EventId,
    [property: JsonConverter(typeof(MicrosecondUtcConverter))] DateTimeOffset OccurredAt,
    string EventType,
    long AggregateVersion,
    Guid ActorUserId,
    string? ActorDisplayName,
    Guid CorrelationId,
    JsonElement? Payload,
    JsonElement? PreviousPayload);

/// <summary>The shared <c>Page</c> envelope of <see cref="AuditEvent"/> (conventions-api.md §4).</summary>
public sealed record AuditEventPage(IReadOnlyList<AuditEvent> Items, int Page, int PageSize, long Total);

/// <summary>
/// Writes a timestamp as ISO-8601 UTC with exactly six fractional digits
/// (<c>2026-10-18T16:20:00.123456Z</c>), the form of the contract's example.
/// </summary>
internal sealed class MicrosecondUtcConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
}
