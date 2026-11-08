using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Avro;
using Avro.Generic;
using Avro.Util;

namespace Nexus.Projections.Projections;

/// <summary>
/// Converts an Avro <see cref="GenericRecord"/> to compact, deterministic JSON (the <c>payload_json</c>
/// of <c>event_log</c>). Field names are exactly the Avro field names, in schema order; map keys are
/// sorted ordinally, so replaying the same event (rebuild) reproduces byte-identical rows.
/// <list type="bullet">
/// <item>union: the non-null branch value (null becomes JSON null)</item>
/// <item>string, uuid: string; boolean; int/long/float/double: JSON number</item>
/// <item>bytes/fixed with logical <c>decimal</c>: JSON STRING with the exact decimal at the schema scale (never a double)</item>
/// <item>timestamp-millis/micros (and local-timestamp-*): <c>yyyy-MM-ddTHH:mm:ss.ffffffZ</c> (UTC); date: <c>yyyy-MM-dd</c></item>
/// <item>enum: symbol; array: array; map: object; plain bytes/fixed: base64</item>
/// </list>
/// Both raw Avro representations and the converted ones (<see cref="Guid"/>, <see cref="AvroDecimal"/>,
/// <see cref="DateTime"/>) are accepted: what <c>GenericReader</c> yields depends on the logical-type
/// registrations of the deserializer.
/// </summary>
public static class AvroJson
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

    /// <summary>Serialises <paramref name="record"/> to a compact JSON string.</summary>
    public static string ToJson(GenericRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = false }))
            WriteValue(writer, record.Schema, record);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter w, Schema schema, object? value)
    {
        if (schema is UnionSchema union)
        {
            if (value is null) { w.WriteNullValue(); return; }
            WriteValue(w, ResolveBranch(union, value), value);
            return;
        }
        if (value is null) { w.WriteNullValue(); return; }

        var logical = schema.GetProperty("logicalType");

        switch (schema.Tag)
        {
            case Schema.Type.Null:
                w.WriteNullValue();
                break;
            case Schema.Type.Boolean:
                w.WriteBooleanValue(Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                break;
            case Schema.Type.String:
                w.WriteStringValue(value switch
                {
                    Guid g => g.ToString("D"),
                    string s => s,
                    _ => throw Unsupported(schema, value),
                });
                break;
            case Schema.Type.Int:
                if (logical == "date") w.WriteStringValue(FormatDate(value));
                else if (logical is "time-millis") throw Unsupported(schema, value);
                else w.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                break;
            case Schema.Type.Long:
                if (logical is "timestamp-millis" or "timestamp-micros" or "local-timestamp-millis" or "local-timestamp-micros")
                    w.WriteStringValue(FormatTimestamp(logical, value));
                else if (logical == "time-micros") throw Unsupported(schema, value);
                else w.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case Schema.Type.Float:
                w.WriteNumberValue(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                break;
            case Schema.Type.Double:
                w.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            case Schema.Type.Bytes:
            case Schema.Type.Fixed:
                WriteBinary(w, schema, logical, value);
                break;
            case Schema.Type.Enumeration:
                w.WriteStringValue(value is GenericEnum e ? e.Value : value.ToString());
                break;
            case Schema.Type.Record:
                WriteRecord(w, (RecordSchema)schema, value);
                break;
            case Schema.Type.Array:
                w.WriteStartArray();
                var items = ((ArraySchema)schema).ItemSchema;
                foreach (var item in (System.Collections.IEnumerable)value)
                    WriteValue(w, items, item);
                w.WriteEndArray();
                break;
            case Schema.Type.Map:
                WriteMap(w, ((MapSchema)schema).ValueSchema, value);
                break;
            default:
                throw Unsupported(schema, value);
        }
    }

    private static void WriteRecord(Utf8JsonWriter w, RecordSchema schema, object value)
    {
        var record = value as GenericRecord ?? throw Unsupported(schema, value);
        w.WriteStartObject();
        foreach (var field in schema.Fields)
        {
            w.WritePropertyName(field.Name);
            record.TryGetValue(field.Name, out var fieldValue);
            WriteValue(w, field.Schema, fieldValue);
        }
        w.WriteEndObject();
    }

    private static void WriteMap(Utf8JsonWriter w, Schema valueSchema, object value)
    {
        IEnumerable<KeyValuePair<string, object?>> entries = value switch
        {
            System.Collections.IDictionary d => d.Cast<System.Collections.DictionaryEntry>()
                .Select(x => new KeyValuePair<string, object?>((string)x.Key, x.Value)),
            _ => throw Unsupported(valueSchema, value),
        };
        w.WriteStartObject();
        foreach (var (key, entryValue) in entries.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            w.WritePropertyName(key);
            WriteValue(w, valueSchema, entryValue);
        }
        w.WriteEndObject();
    }

    private static void WriteBinary(Utf8JsonWriter w, Schema schema, string? logical, object value)
    {
        var bytes = value switch
        {
            byte[] b => b,
            GenericFixed f => f.Value,
            _ => null,
        };

        if (logical == "decimal")
        {
            switch (value)
            {
                case AvroDecimal ad:
                    w.WriteStringValue(FormatUnscaled(ad.UnscaledValue, ad.Scale));
                    return;
                case decimal d:
                    // Re-render at the schema scale so "1.5" and "1.50" cannot differ between representations.
                    w.WriteStringValue(d.ToString("F" + SchemaScale(schema).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
                    return;
            }
            if (bytes is null) throw Unsupported(schema, value);
            // Avro decimal: big-endian two's complement unscaled value; BigInteger wants little-endian.
            var unscaled = new BigInteger(bytes, isUnsigned: false, isBigEndian: true);
            w.WriteStringValue(FormatUnscaled(unscaled, SchemaScale(schema)));
            return;
        }

        if (bytes is null) throw Unsupported(schema, value);
        w.WriteStringValue(Convert.ToBase64String(bytes));
    }

    private static int SchemaScale(Schema schema)
    {
        var scale = schema.GetProperty("scale");
        return scale is null ? 0 : int.Parse(scale, CultureInfo.InvariantCulture);
    }

    /// <summary>Renders <paramref name="unscaled"/> / 10^<paramref name="scale"/> exactly, always with <paramref name="scale"/> decimals.</summary>
    internal static string FormatUnscaled(BigInteger unscaled, int scale)
    {
        var negative = unscaled.Sign < 0;
        var digits = BigInteger.Abs(unscaled).ToString(CultureInfo.InvariantCulture);
        if (scale > 0)
        {
            digits = digits.PadLeft(scale + 1, '0');
            digits = digits[..^scale] + "." + digits[^scale..];
        }
        return negative ? "-" + digits : digits;
    }

    private static string FormatTimestamp(string logical, object value)
    {
        DateTime utc;
        switch (value)
        {
            case DateTime dt:
                utc = dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                break;
            case DateTimeOffset dto:
                utc = dto.UtcDateTime;
                break;
            default:
                var n = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                var ticks = logical.EndsWith("micros", StringComparison.Ordinal) ? n * 10 : n * TimeSpan.TicksPerMillisecond;
                utc = DateTime.UnixEpoch.AddTicks(ticks);
                break;
        }
        return utc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    private static string FormatDate(object value) => value switch
    {
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => DateTime.UnixEpoch.AddDays(Convert.ToInt32(value, CultureInfo.InvariantCulture))
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>Picks the union branch for a non-null value: the only non-null branch, else the one matching the runtime type.</summary>
    private static Schema ResolveBranch(UnionSchema union, object value)
    {
        var branches = union.Schemas.Where(s => s.Tag != Schema.Type.Null).ToList();
        if (branches.Count == 1) return branches[0];
        foreach (var branch in branches)
            if (Matches(branch, value)) return branch;
        throw Unsupported(union, value);
    }

    private static bool Matches(Schema s, object v) => s.Tag switch
    {
        Schema.Type.Boolean => v is bool,
        Schema.Type.String => v is string or Guid,
        Schema.Type.Int => v is int,
        Schema.Type.Long => v is long or DateTime or DateTimeOffset,
        Schema.Type.Float => v is float,
        Schema.Type.Double => v is double,
        Schema.Type.Bytes => v is byte[] or AvroDecimal or decimal,
        Schema.Type.Fixed => v is GenericFixed,
        Schema.Type.Enumeration => v is GenericEnum,
        Schema.Type.Record => v is GenericRecord r && r.Schema.Fullname == s.Fullname,
        Schema.Type.Array => v is System.Collections.IList,
        Schema.Type.Map => v is System.Collections.IDictionary,
        _ => false,
    };

    private static NotSupportedException Unsupported(Schema schema, object value) =>
        new($"Cannot convert value of type {value.GetType().Name} for Avro schema {schema.Name ?? schema.Tag.ToString()} to JSON.");
}
