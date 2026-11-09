using System.Globalization;

namespace Nexus.Projections.Projections;

/// <summary>
/// Thrown at startup when <c>contracts/events/topics.yaml</c> does not have the structure the
/// projections rely on (fail fast: a wrong pii flag would leak personal data into ClickHouse).
/// </summary>
public sealed class TopicRegistryException(string message) : Exception(message);

/// <summary>
/// The pii flags of <c>contracts/events/topics.yaml</c> (conventions-events.md §5, EV-R5). Used by
/// <see cref="EventLogProjection"/> to decide whether a payload may be stored.
/// <para>
/// A deliberately minimal STRICT parser for the known structure: a top-level <c>topics:</c> list whose
/// entries start with <c>- name:</c> (two-space indent) and carry <c>pii:</c> among other scalar keys
/// (four-space indent). Deeper lines (the nested <c>partitions:</c> map) and comments are ignored.
/// It is strict on what matters: every entry needs a name and a <c>pii</c> of exactly
/// <c>true</c>/<c>false</c>, names are unique and the list is not empty.
/// </para>
/// </summary>
public sealed class TopicRegistry
{
    private readonly Dictionary<string, bool> _pii;

    private TopicRegistry(Dictionary<string, bool> pii) => _pii = pii;

    /// <summary>Default location: <c>contracts/events/topics.yaml</c> next to the binaries (copied by the csproj).</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "contracts", "events", "topics.yaml");

    /// <summary>Number of registered topics.</summary>
    public int Count => _pii.Count;

    /// <summary>Loads and validates the registry from <paramref name="path"/> (default: <see cref="DefaultPath"/>).</summary>
    public static TopicRegistry LoadFile(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            throw new TopicRegistryException($"Topic registry not found at '{path}'.");
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Parses and validates registry content. <paramref name="source"/> is only used in error messages.</summary>
    public static TopicRegistry Parse(string yaml, string source = "topics.yaml")
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        var inTopics = false;
        string? name = null;
        string? pii = null;
        var entryLine = 0;
        var lineNo = 0;

        void Flush()
        {
            if (entryLine == 0) return;
            if (string.IsNullOrEmpty(name))
                throw new TopicRegistryException($"{source}:{entryLine}: topic entry without 'name'.");
            if (pii is not ("true" or "false"))
                throw new TopicRegistryException(
                    $"{source}:{entryLine}: topic '{name}' has missing or invalid 'pii' (expected true/false, got '{pii ?? "<missing>"}').");
            if (!result.TryAdd(name, pii == "true"))
                throw new TopicRegistryException($"{source}:{entryLine}: duplicate topic '{name}'.");
            name = null;
            pii = null;
            entryLine = 0;
        }

        foreach (var raw in yaml.Split('\n'))
        {
            lineNo++;
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            var indent = line.Length - line.TrimStart(' ').Length;

            if (indent == 0)
            {
                Flush();
                inTopics = StripComment(trimmed) == "topics:";
                continue;
            }
            if (!inTopics) continue;

            if (indent == 2 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush();
                entryLine = lineNo;
                var (key, value) = SplitKeyValue(trimmed[2..], source, lineNo);
                if (key == "name") name = value;
                else if (key == "pii") pii = value;
            }
            else if (indent == 4 && entryLine != 0)
            {
                var (key, value) = SplitKeyValue(trimmed, source, lineNo);
                if (key == "name") name = value;
                else if (key == "pii") pii = value;
            }
            // Deeper lines (partitions map) and unexpected shapes are ignored on purpose.
        }
        Flush();

        if (result.Count == 0)
            throw new TopicRegistryException($"{source}: no topics found.");
        return new TopicRegistry(result);
    }

    /// <summary>True when the topic is in the registry.</summary>
    public bool IsKnown(string topic) => _pii.ContainsKey(topic);

    /// <summary>
    /// True when the payload of <paramref name="topic"/> is personal data. Fails closed: a topic that is
    /// not in the registry is treated as PII.
    /// </summary>
    public bool IsPii(string topic) => !_pii.TryGetValue(topic, out var pii) || pii;

    private static (string Key, string Value) SplitKeyValue(string text, string source, int lineNo)
    {
        var s = StripComment(text);
        var i = s.IndexOf(':', StringComparison.Ordinal);
        if (i <= 0)
            throw new TopicRegistryException($"{source}:{lineNo}: expected 'key: value', got '{s}'.");
        var key = s[..i].Trim();
        var value = s[(i + 1)..].Trim();
        if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
            value = value[1..^1];
        return (key, value.ToString(CultureInfo.InvariantCulture));
    }

    private static string StripComment(string s)
    {
        var i = s.IndexOf(" #", StringComparison.Ordinal);
        return (i >= 0 ? s[..i] : s).TrimEnd();
    }
}
