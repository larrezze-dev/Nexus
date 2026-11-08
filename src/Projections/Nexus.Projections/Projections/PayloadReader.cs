using System.Globalization;
using Avro.Generic;

namespace Nexus.Projections.Projections;

/// <summary>
/// Typed access to the fields of an Avro payload for the projections. Every method throws
/// <see cref="InvalidOperationException"/> with the field name when the field is missing or has the
/// wrong shape, so the runtime can retry and then dead-letter the event (contract violation).
/// Accepts both raw (string) and converted (<see cref="Guid"/>) UUID representations.
/// </summary>
internal static class PayloadReader
{
    public static Guid Guid(GenericRecord record, string field) =>
        TryGuid(record, field) ?? throw new InvalidOperationException($"Payload field '{field}' is missing or null.");

    /// <summary>Nullable UUID: null when the field is absent or null; throws when present but not a UUID.</summary>
    public static Guid? TryGuid(GenericRecord record, string field)
    {
        if (!record.TryGetValue(field, out var value) || value is null) return null;
        return value switch
        {
            System.Guid g => g,
            string s when System.Guid.TryParse(s, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Payload field '{field}' is not a valid UUID."),
        };
    }

    public static string String(GenericRecord record, string field) =>
        record.TryGetValue(field, out var value) && value is string s
            ? s
            : throw new InvalidOperationException($"Payload field '{field}' is missing or not a string.");

    public static bool Bool(GenericRecord record, string field) =>
        record.TryGetValue(field, out var value) && value is bool b
            ? b
            : throw new InvalidOperationException($"Payload field '{field}' is missing or not a boolean.");

    /// <summary>Avro map of strings to <c>Dictionary&lt;string,string&gt;</c> (ClickHouse Map column).</summary>
    public static Dictionary<string, string> StringMap(GenericRecord record, string field)
    {
        if (!record.TryGetValue(field, out var value) || value is not System.Collections.IDictionary dict)
            throw new InvalidOperationException($"Payload field '{field}' is missing or not a map.");
        var result = new Dictionary<string, string>(dict.Count, StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry e in dict)
            result[(string)e.Key] = Convert.ToString(e.Value, CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException($"Payload map '{field}' has a null value for key '{e.Key}'.");
        return result;
    }

    /// <summary>Converts the envelope's signed version to the table's UInt64; a negative version is a contract violation.</summary>
    public static ulong Version(long aggregateVersion) =>
        aggregateVersion >= 0
            ? (ulong)aggregateVersion
            : throw new InvalidOperationException($"Negative aggregate version {aggregateVersion}.");
}
