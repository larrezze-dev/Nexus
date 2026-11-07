using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;

#pragma warning disable CA1031 // Failures are classified and reported as PayloadEncodingException by design.
using Avro;
using Avro.Generic;
using Avro.IO;
using Confluent.SchemaRegistry;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Schema = Avro.Schema;

namespace Nexus.OutboxRelay.Publishing;

/// <summary>
/// Avro JSON encoding (the <c>payload</c> column, EV-R1) -&gt; Confluent wire format, with the
/// writer schema resolved <b>by content</b> (ADR-0018, conventions-events.md §6):
/// <list type="number">
///   <item>The local <c>.avsc</c> the <c>OutboxWriter</c> validated the payload against (EV-R2) is
///   loaded from <see cref="RelayOptions.ContractsDirectory"/> with the same path convention as
///   <see cref="FileSystemEventSchemaProvider"/>: <c>event_type</c>
///   <c>&lt;context&gt;.&lt;aggregate&gt;.&lt;event&gt;</c> + <c>schema_version</c> n ⇒
///   <c>&lt;root&gt;/&lt;context&gt;/&lt;aggregate&gt;.&lt;event&gt;.v&lt;n&gt;.avsc</c>.</item>
///   <item>Its registry id is obtained with <c>POST /subjects/&lt;topic&gt;-value</c>
///   (<see cref="ISchemaRegistryClient.LookupSchemaAsync"/>), subject per TopicNameStrategy.
///   Never <c>GET /subjects/&lt;topic&gt;-value/versions/&lt;n&gt;</c>: the file's <c>v&lt;n&gt;</c> is
///   not the subject's version number (an in-place compatible change, a new <c>.v2</c> topic and
///   idempotent registration all decouple them).</item>
///   <item>The payload is decoded and re-encoded with that exact parsed local schema.</item>
/// </list>
///
/// <para>
/// Wire format: magic byte <c>0</c>, the 4-byte big-endian schema id, then the Avro binary
/// encoding. Only the Apache.Avro generic API is used (decode JSON with the writer schema,
/// re-encode binary with the same schema): no record is reshaped. <c>Confluent.SchemaRegistry.Serdes.Avro</c>'s
/// serializer is not used because it would take the schema from the datum, not from the row.
/// </para>
///
/// <para>
/// Failure classification (decision D-H1, conventions-events.md §4 step 5): a missing or
/// unparseable local <c>.avsc</c>, content not registered in the subject (40401 / 40403), any
/// other 4xx from the registry and a payload that does not decode/encode are <b>row</b> failures
/// (non-transient: they count towards <c>FAILED</c>). Registry unreachable, 5xx and timeouts are
/// <b>transient</b>: they never make a row <c>FAILED</c>.
/// </para>
///
/// <para>
/// Successful resolutions are cached per (subject, event_type, schema_version) for the life of the
/// process (the <c>.avsc</c> files are immutable inside an image, and a registered schema keeps
/// its id); failures are never cached, so registering the schema later fixes the row's next attempt.
/// </para>
/// </summary>
public sealed class SchemaRegistryPayloadEncoder : IPayloadEncoder
{
    private const byte MagicByte = 0;

    // Confluent Schema Registry "not found" error codes: the content is not registered in the
    // subject. Not transient: retrying will not make it appear until someone registers it.
    private const int SubjectNotFound = 40401;
    private const int SchemaNotFound = 40403;

    private readonly ISchemaRegistryClient _registry;
    private readonly FileSystemEventSchemaProvider _paths;
    private readonly ConcurrentDictionary<(string Subject, string EventType, int SchemaVersion), WriterSchema> _cache = new();

    /// <param name="registry">Schema Registry client.</param>
    /// <param name="contractsDirectory">Root laid out like <c>contracts/events</c> (see <see cref="RelayOptions.ContractsDirectory"/>).</param>
    public SchemaRegistryPayloadEncoder(ISchemaRegistryClient registry, string contractsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractsDirectory);
        _registry = registry;
        _paths = new FileSystemEventSchemaProvider(contractsDirectory);
    }

    public static string SubjectFor(string topic) => $"{topic}-value";

    /// <summary>Registry client configuration for the relay: bounded request time, so an unreachable registry fails a row fast.</summary>
    public static SchemaRegistryConfig CreateClientConfig(RelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SchemaRegistryConfig { Url = options.SchemaRegistryUrl, RequestTimeoutMs = 10_000 };
    }

    public async Task<byte[]> EncodeAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var writer = await GetWriterSchemaAsync(SubjectFor(message.Topic), message.EventType, message.SchemaVersion).ConfigureAwait(false);
        return Encode(writer, message.Payload);
    }

    /// <summary>JSON -&gt; wire format with an already resolved writer schema (exposed for tests).</summary>
    public static byte[] Encode(WriterSchema writer, string avroJsonPayload)
    {
        ArgumentNullException.ThrowIfNull(writer);
        object datum;
        try
        {
            var reader = new GenericDatumReader<object>(writer.Schema, writer.Schema);
            datum = reader.Read(null!, new JsonDecoder(writer.Schema, avroJsonPayload));
        }
        // Any decoding failure is about this row's data (Avro's JSON decoder throws a variety of
        // exception types on malformed input), never about the infrastructure.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PayloadEncodingException(
                $"Payload does not decode with schema id {writer.Id} ({writer.Schema.Fullname}): {ex.GetType().Name}.",
                isTransient: false,
                ex);
        }

        using var stream = new MemoryStream();
        stream.WriteByte(MagicByte);
        Span<byte> id = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(id, writer.Id);
        stream.Write(id);

        try
        {
            new GenericDatumWriter<object>(writer.Schema).Write(datum, new BinaryEncoder(stream));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PayloadEncodingException($"Payload does not encode with schema id {writer.Id}: {ex.GetType().Name}.", isTransient: false, ex);
        }

        return stream.ToArray();
    }

    private async Task<WriterSchema> GetWriterSchemaAsync(string subject, string eventType, int schemaVersion)
    {
        var key = (subject, eventType, schemaVersion);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var (path, schemaJson, schema) = LoadLocalSchema(eventType, schemaVersion);

        RegisteredSchema registered;
        try
        {
            registered = await _registry.LookupSchemaAsync(subject, new Confluent.SchemaRegistry.Schema(schemaJson, SchemaType.Avro), ignoreDeletedSchemas: true).ConfigureAwait(false);
        }
        catch (SchemaRegistryException ex) when (ex.ErrorCode is SubjectNotFound or SchemaNotFound)
        {
            throw new PayloadEncodingException(
                $"Schema '{path}' is not registered in subject '{subject}' (error {ex.ErrorCode}); register it (tools/register-schemas.sh).",
                isTransient: false,
                ex);
        }
        catch (SchemaRegistryException ex) when (ex.Status is not (>= HttpStatusCode.InternalServerError))
        {
            throw new PayloadEncodingException(
                $"Schema Registry rejected the lookup of '{path}' in subject '{subject}' (HTTP {(int)ex.Status}, error {ex.ErrorCode}).",
                isTransient: false,
                ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 5xx, connection refused, DNS, timeouts — and anything unexpected from the client
            // (Confluent.SchemaRegistry 2.16 throws NullReferenceException when MaxRetries = 0 and
            // the registry is unreachable): "registry unavailable", so the batch stops and backs
            // off, and the row is never made FAILED by it (D-H1).
            throw new PayloadEncodingException($"Schema Registry unavailable looking up subject '{subject}': {ex.GetType().Name}.", isTransient: true, ex);
        }

        var writer = new WriterSchema(registered.Id, schema);
        _cache.TryAdd(key, writer);
        return writer;
    }

    /// <summary>Reads and parses the row's local <c>.avsc</c>; every failure is a row failure (ADR-0018 §4).</summary>
    private (string Path, string Json, Schema Schema) LoadLocalSchema(string eventType, int schemaVersion)
    {
        string path;
        try
        {
            path = _paths.ResolvePath(eventType, schemaVersion);
        }
        catch (EventSchemaNotFoundException ex)
        {
            throw new PayloadEncodingException($"No local Avro schema for event '{eventType}' v{schemaVersion}: {ex.Message}", isTransient: false, ex);
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new PayloadEncodingException(
                $"No local Avro schema for event '{eventType}' v{schemaVersion}: expected '{path}' (contracts/events must ship with the relay, ADR-0018).",
                isTransient: false,
                ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PayloadEncodingException($"Local Avro schema '{path}' could not be read: {ex.GetType().Name}.", isTransient: false, ex);
        }

        try
        {
            var schema = Schema.Parse(json);
            return schema is RecordSchema
                ? (path, json, schema)
                : throw new PayloadEncodingException($"Local Avro schema '{path}' is a {schema.Tag}, not a record.", isTransient: false);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or PayloadEncodingException))
        {
            throw new PayloadEncodingException($"Local Avro schema '{path}' is not parseable: {ex.GetType().Name}.", isTransient: false, ex);
        }
    }
}

/// <summary>A registry schema id and its parsed Avro schema.</summary>
public sealed record WriterSchema(int Id, Schema Schema);
