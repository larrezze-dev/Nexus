using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Avro;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Persistence.Outbox;

/// <summary>
/// Resolves the local Avro schema (<c>.avsc</c>) an event's payload must conform to before it
/// is written to the outbox (EV-R2).
/// </summary>
public interface IEventSchemaProvider
{
    /// <summary>
    /// Returns the record schema for <paramref name="domainEvent"/>. Throws
    /// <see cref="EventSchemaNotFoundException"/> when no schema exists: an event without a
    /// schema can never be written, which aborts the unit of work.
    /// </summary>
    RecordSchema GetSchema(IDomainEvent domainEvent);
}

/// <summary>
/// Resolves schemas from a directory laid out like <c>contracts/events/</c>
/// (conventions-events.md §6, repo-structure.md §3), by convention:
/// <code>
/// EventType "&lt;context&gt;.&lt;aggregate&gt;.&lt;event&gt;" + SchemaVersion n
///   =&gt; &lt;root&gt;/&lt;context&gt;/&lt;aggregate&gt;.&lt;event&gt;.v&lt;n&gt;.avsc
/// </code>
/// The three segments are taken verbatim from <see cref="IDomainEvent.EventType"/> (no
/// snake/kebab conversion). Each segment must match <c>[a-z][a-z0-9_-]*</c>, which also rules
/// out path traversal. Parsed schemas are cached for the lifetime of the provider (register it
/// as a singleton): <c>.avsc</c> files are build-time contract artefacts, not runtime data.
///
/// The host decides where the root is (typically <c>contracts/events</c> copied next to the
/// service binaries at build time).
/// </summary>
public sealed partial class FileSystemEventSchemaProvider : IEventSchemaProvider
{
    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<(string EventType, int SchemaVersion), RecordSchema> _cache = new();

    public FileSystemEventSchemaProvider(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public RecordSchema GetSchema(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return _cache.GetOrAdd((domainEvent.EventType, domainEvent.SchemaVersion), Load);
    }

    /// <summary>The path the convention maps an event type and version to (exposed for diagnostics and tests).</summary>
    public string ResolvePath(string eventType, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        if (schemaVersion < 1)
        {
            throw new EventSchemaNotFoundException(
                $"Event '{eventType}' has schema version {schemaVersion}; versions start at 1 (v<n> of the .avsc file).");
        }

        var segments = eventType.Split('.');
        if (segments.Length != 3 || !segments.All(s => SegmentPattern().IsMatch(s)))
        {
            throw new EventSchemaNotFoundException(
                $"Event type '{eventType}' does not follow '<context>.<aggregate>.<event>' with segments matching [a-z][a-z0-9_-]*.");
        }

        var path = Path.GetFullPath(Path.Combine(
            _rootDirectory, segments[0], $"{segments[1]}.{segments[2]}.v{schemaVersion}.avsc"));

        // Defence in depth: the segment pattern already forbids '.', '/' and '\'.
        if (!path.StartsWith(_rootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new EventSchemaNotFoundException($"Event type '{eventType}' resolves outside the schema root.");
        }

        return path;
    }

    private RecordSchema Load((string EventType, int SchemaVersion) key)
    {
        var path = ResolvePath(key.EventType, key.SchemaVersion);
        if (!File.Exists(path))
        {
            throw new EventSchemaNotFoundException(
                $"No Avro schema for event '{key.EventType}' v{key.SchemaVersion}: expected '{path}'.");
        }

        var schema = Schema.Parse(File.ReadAllText(path));
        return schema as RecordSchema
            ?? throw new EventSchemaNotFoundException(
                $"Schema '{path}' is a {schema.Tag}, but an event payload schema must be an Avro record.");
    }

    // \z (not $): $ allows a trailing \n in .NET regex, \z anchors to the true end of the string.
    [GeneratedRegex(@"^[a-z][a-z0-9_-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();
}

/// <summary>No usable local <c>.avsc</c> exists for an event (EV-R2: the event cannot be written).</summary>
public sealed class EventSchemaNotFoundException : Exception
{
    public EventSchemaNotFoundException()
    {
    }

    public EventSchemaNotFoundException(string message)
        : base(message)
    {
    }

    public EventSchemaNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
