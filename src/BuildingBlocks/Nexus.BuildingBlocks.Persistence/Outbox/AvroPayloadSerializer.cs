using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using Avro;
using Avro.Generic;
using Avro.IO;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Persistence.Outbox;

/// <summary>
/// The IDomainEvent -&gt; Avro bridge (EV-R1, EV-R2). Schema-driven, reflection-based:
///
/// <para>
/// The payload of an event is the set of public instance properties of its concrete CLR type.
/// The <c>.avsc</c> record drives the mapping: every Avro field must be matched by exactly one
/// public property (case-insensitive name match, so Avro <c>assetTypeId</c> &lt;-&gt; C#
/// <c>AssetTypeId</c>), and every public property must be matched by a field, except the
/// <see cref="IDomainEvent"/> envelope members (<c>AggregateType</c>, <c>AggregateId</c>,
/// <c>EventType</c>, <c>SchemaVersion</c>, <c>Topic</c>, <c>LegalEntityId</c>), which travel in
/// the envelope and are only copied into the payload when the schema declares a field with that
/// name (e.g. EV-R4 full-state master-data events). Anything else - a missing field, an unmapped
/// property, a value of the wrong type, a null in a non-nullable field, a decimal that does not
/// fit the declared precision/scale - throws <see cref="EventPayloadSchemaException"/>, which
/// aborts the unit of work (EV-R2).
/// </para>
///
/// <para>CLR -&gt; Avro mapping (conventions-events.md §6):</para>
/// <list type="bullet">
///   <item><c>uuid</c>: <see cref="Guid"/>.</item>
///   <item><c>timestamp-micros</c> / <c>timestamp-millis</c>: <see cref="DateTimeOffset"/>, or
///   <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/> only (any other kind is rejected
///   instead of being silently shifted).</item>
///   <item><c>date</c>: <see cref="DateOnly"/>.</item>
///   <item><c>decimal</c>: <see cref="decimal"/>, rescaled exactly to the schema scale; values
///   with more fractional digits than the scale, or more digits than the precision, are
///   rejected (never rounded).</item>
///   <item><c>string</c>: <see cref="string"/> only (CLR enums are rejected: states travel as
///   strings and the domain decides their spelling). <c>boolean</c>/<c>int</c>/<c>long</c>/
///   <c>float</c>/<c>double</c>/<c>bytes</c>: the matching CLR primitive (<c>int</c> widens to
///   <c>long</c>).</item>
///   <item><c>array</c>: any non-string <see cref="IEnumerable"/>. <c>map</c>: any
///   <see cref="IDictionary"/> with string keys (e.g. translatable texts). Nested
///   <c>record</c>: any object, mapped with the same strict rules. Unions: <see langword="null"/>
///   selects the null branch; otherwise the first branch the value converts to.</item>
/// </list>
///
/// <para>
/// The resulting <see cref="GenericRecord"/> is written with Avro's own
/// <see cref="GenericDatumWriter{T}"/> + <see cref="JsonEncoder"/>, which re-validates every
/// value against the schema and produces the canonical Avro JSON encoding stored in
/// <c>outbox.message.payload</c> (EV-R1); the relay turns it into binary Avro later. That column
/// is <c>text</c>, not <c>jsonb</c> (ADR-0013): decimals are encoded as <c>bytes</c>, written as
/// <c>\u00XX</c> escapes, and jsonb rejects the <c>\u0000</c> escape that ordinary amounts
/// such as 16.0000 produce.
/// </para>
///
/// <para>
/// Why this design: domain events live in each service's Domain layer, which must not depend on
/// Avro or on persistence; a schema-driven mapper keeps them plain C# records while still
/// validating against the contract, and needs no per-event registration.
/// </para>
/// </summary>
internal static class AvroPayloadSerializer
{
    private static readonly HashSet<string> EnvelopeMembers = new(
        typeof(IDomainEvent).GetProperties().Select(p => p.Name),
        StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> PropertyCache = new();

    /// <summary>Serializes <paramref name="domainEvent"/> to Avro JSON encoding against <paramref name="schema"/>.</summary>
    public static string Serialize(IDomainEvent domainEvent, RecordSchema schema)
    {
        var record = ToRecord(domainEvent, schema, path: domainEvent.EventType, isEnvelope: true);

        try
        {
            using var stream = new MemoryStream();
            var encoder = new JsonEncoder(schema, stream);
            new GenericDatumWriter<GenericRecord>(schema).Write(record, encoder);
            encoder.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (AvroException ex)
        {
            throw new EventPayloadSchemaException(
                $"Event '{domainEvent.EventType}' does not conform to schema '{schema.Fullname}': {ex.Message}", ex);
        }
    }

    private static GenericRecord ToRecord(object source, RecordSchema schema, string path, bool isEnvelope)
    {
        var properties = GetProperties(source.GetType(), path);
        var record = new GenericRecord(schema);
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in schema.Fields)
        {
            var fieldPath = $"{path}.{field.Name}";
            if (!properties.TryGetValue(field.Name, out var property))
            {
                throw new EventPayloadSchemaException(
                    $"'{fieldPath}': schema field has no matching public property on {source.GetType().Name}.");
            }

            mapped.Add(property.Name);
            record.Add(field.Name, Convert(property.GetValue(source), field.Schema, fieldPath));
        }

        var unmapped = properties.Values
            .Where(p => !mapped.Contains(p.Name) && !(isEnvelope && EnvelopeMembers.Contains(p.Name)))
            .Select(p => p.Name)
            .ToList();
        if (unmapped.Count > 0)
        {
            throw new EventPayloadSchemaException(
                $"'{path}': public properties [{string.Join(", ", unmapped)}] of {source.GetType().Name} are not in schema '{schema.Fullname}'.");
        }

        return record;
    }

    private static Dictionary<string, PropertyInfo> GetProperties(Type type, string path) =>
        PropertyCache.GetOrAdd(type, t =>
        {
            var result = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
                {
                    continue;
                }

                if (!result.TryAdd(property.Name, property))
                {
                    throw new EventPayloadSchemaException(
                        $"'{path}': {t.Name} has public properties whose names differ only by case ('{property.Name}').");
                }
            }

            return result;
        });

    private static object? Convert(object? value, Schema schema, string path)
    {
        switch (schema)
        {
            case UnionSchema union:
                return ConvertUnion(value, union, path);
            case LogicalSchema logical:
                return ConvertLogical(value, logical, path);
        }

        if (schema.Tag == Schema.Type.Null)
        {
            return value is null ? null : throw Mismatch(path, schema, value);
        }

        if (value is null)
        {
            throw new EventPayloadSchemaException($"'{path}': null is not allowed by non-nullable schema '{schema.Name}'.");
        }

        return schema.Tag switch
        {
            Schema.Type.Boolean => value as bool? ?? throw Mismatch(path, schema, value),
            Schema.Type.Int => value as int? ?? throw Mismatch(path, schema, value),
            Schema.Type.Long => value switch
            {
                long l => l,
                int i => (long)i,
                _ => throw Mismatch(path, schema, value),
            },
            Schema.Type.Float => value as float? ?? throw Mismatch(path, schema, value),
            Schema.Type.Double => value as double? ?? throw Mismatch(path, schema, value),
            Schema.Type.String => value as string ?? throw Mismatch(path, schema, value),
            Schema.Type.Bytes => value as byte[] ?? throw Mismatch(path, schema, value),
            Schema.Type.Fixed => ConvertFixed(value, (FixedSchema)schema, path),
            Schema.Type.Enumeration => ConvertEnum(value, (EnumSchema)schema, path),
            Schema.Type.Array => ConvertArray(value, (ArraySchema)schema, path),
            Schema.Type.Map => ConvertMap(value, (MapSchema)schema, path),
            Schema.Type.Record => ToRecord(value, (RecordSchema)schema, path, isEnvelope: false),
            _ => throw new EventPayloadSchemaException($"'{path}': Avro type '{schema.Tag}' is not supported in event payloads."),
        };
    }

    private static object? ConvertUnion(object? value, UnionSchema union, string path)
    {
        if (value is null)
        {
            return union.Schemas.Any(s => s.Tag == Schema.Type.Null)
                ? null
                : throw new EventPayloadSchemaException($"'{path}': null is not a branch of union {union}.");
        }

        var errors = new List<string>();
        foreach (var branch in union.Schemas.Where(s => s.Tag != Schema.Type.Null))
        {
            try
            {
                return Convert(value, branch, path);
            }
            catch (EventPayloadSchemaException ex)
            {
                errors.Add(ex.Message);
            }
        }

        throw new EventPayloadSchemaException(
            $"'{path}': value of type {value.GetType().Name} matches no branch of union {union}: {string.Join(" | ", errors)}");
    }

    private static object ConvertLogical(object? value, LogicalSchema schema, string path)
    {
        if (value is null)
        {
            throw new EventPayloadSchemaException($"'{path}': null is not allowed by non-nullable logical type '{schema.LogicalTypeName}'.");
        }

        switch (schema.LogicalTypeName)
        {
            case "uuid":
                return value as Guid? ?? throw Mismatch(path, schema, value);

            case "timestamp-micros":
            case "timestamp-millis":
            {
                var utc = value switch
                {
                    DateTimeOffset dto => dto.UtcDateTime,
                    DateTime { Kind: DateTimeKind.Utc } dt => dt,
                    DateTime dt => throw new EventPayloadSchemaException(
                        $"'{path}': DateTime with Kind={dt.Kind} is ambiguous; use DateTimeOffset or a UTC DateTime."),
                    _ => throw Mismatch(path, schema, value),
                };

                // Reject, don't silently round: same "no precision loss" policy as decimal below.
                // A .NET tick is 100ns; timestamp-millis keeps whole milliseconds (10,000 ticks),
                // timestamp-micros keeps whole microseconds (10 ticks) -- Avro's own encoder would
                // otherwise truncate anything finer via integer division with no error.
                var unitTicks = schema.LogicalTypeName == "timestamp-millis" ? TimeSpan.TicksPerMillisecond : 10L;
                if (utc.Ticks % unitTicks != 0)
                {
                    throw new EventPayloadSchemaException(
                        $"'{path}': value has more precision than '{schema.LogicalTypeName}' can represent " +
                        $"without rounding (ticks={utc.Ticks}); round it explicitly before raising the event.");
                }

                return utc;
            }

            case "date":
                return value is DateOnly d
                    ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                    : throw Mismatch(path, schema, value);

            case "decimal":
                return value is decimal m ? ToAvroDecimal(m, schema, path) : throw Mismatch(path, schema, value);

            default:
                throw new EventPayloadSchemaException($"'{path}': logical type '{schema.LogicalTypeName}' is not supported in event payloads.");
        }
    }

    private static AvroDecimal ToAvroDecimal(decimal value, LogicalSchema schema, string path)
    {
        var scale = int.Parse(schema.GetProperty("scale") ?? "0", CultureInfo.InvariantCulture);
        var precision = int.Parse(
            schema.GetProperty("precision") ?? throw new EventPayloadSchemaException($"'{path}': decimal schema without precision."),
            CultureInfo.InvariantCulture);

        // Exact rescale: never round money or quantities to fit the contract.
        if (decimal.Round(value, scale, MidpointRounding.ToZero) != value)
        {
            throw new EventPayloadSchemaException($"'{path}': {value} has more than {scale} fractional digits (schema scale {scale}).");
        }

        var unscaled = ToUnscaled(value, scale);
        var digits = BigInteger.Abs(unscaled).ToString(CultureInfo.InvariantCulture).TrimStart('0').Length;
        if (digits > precision)
        {
            throw new EventPayloadSchemaException($"'{path}': {value} exceeds decimal precision {precision} (scale {scale}).");
        }

        return new AvroDecimal(unscaled, scale);
    }

    private static BigInteger ToUnscaled(decimal value, int scale)
    {
        // decimal = (-1)^sign * mantissa(96 bit) / 10^currentScale
        var bits = decimal.GetBits(value);
        var mantissa = new BigInteger((uint)bits[0])
            | (new BigInteger((uint)bits[1]) << 32)
            | (new BigInteger((uint)bits[2]) << 64);
        var currentScale = (bits[3] >> 16) & 0xFF;
        var negative = (bits[3] & unchecked((int)0x80000000)) != 0;

        var unscaled = currentScale <= scale
            ? mantissa * BigInteger.Pow(10, scale - currentScale)
            : mantissa / BigInteger.Pow(10, currentScale - scale); // exact: trailing digits verified to be zero

        return negative ? -unscaled : unscaled;
    }

    private static GenericFixed ConvertFixed(object value, FixedSchema schema, string path) =>
        value is byte[] bytes && bytes.Length == schema.Size
            ? new GenericFixed(schema, bytes)
            : throw Mismatch(path, schema, value);

    private static GenericEnum ConvertEnum(object value, EnumSchema schema, string path) =>
        value is string symbol && schema.Contains(symbol)
            ? new GenericEnum(schema, symbol)
            : throw Mismatch(path, schema, value);

    private static object?[] ConvertArray(object value, ArraySchema schema, string path)
    {
        if (value is string or not IEnumerable)
        {
            throw Mismatch(path, schema, value);
        }

        var items = new List<object?>();
        var index = 0;
        foreach (var item in (IEnumerable)value)
        {
            items.Add(Convert(item, schema.ItemSchema, $"{path}[{index++}]"));
        }

        return [.. items];
    }

    private static Dictionary<string, object?> ConvertMap(object value, MapSchema schema, string path)
    {
        if (value is not IDictionary dictionary)
        {
            throw Mismatch(path, schema, value);
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                throw new EventPayloadSchemaException($"'{path}': Avro map keys must be strings, found {entry.Key.GetType().Name}.");
            }

            result[key] = Convert(entry.Value, schema.ValueSchema, $"{path}[\"{key}\"]");
        }

        return result;
    }

    private static EventPayloadSchemaException Mismatch(string path, Schema schema, object value) =>
        new($"'{path}': value of CLR type {value.GetType().Name} does not match Avro schema {schema}.");
}

/// <summary>
/// An event payload does not conform to its local Avro schema (EV-R2). Never caught by the
/// unit of work: it propagates, the transaction rolls back, and nothing is persisted.
/// </summary>
public sealed class EventPayloadSchemaException : Exception
{
    public EventPayloadSchemaException()
    {
    }

    public EventPayloadSchemaException(string message)
        : base(message)
    {
    }

    public EventPayloadSchemaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
