using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;

#pragma warning disable CA1031 // Failures are classified and reported as PayloadEncodingException by design.
using Avro;
using Avro.Generic;
using Avro.IO;
using Confluent.SchemaRegistry;
using Schema = Avro.Schema;

namespace Nexus.OutboxRelay.Publishing;

/// <summary>
/// Avro JSON encoding (the <c>payload</c> column, EV-R1) -&gt; Confluent wire format, with the
/// writer schema taken from the Schema Registry: subject <c>&lt;topic&gt;-value</c>
/// (TopicNameStrategy, conventions-events.md §6), version <c>schema_version</c> of the row (ficha
/// 0.7 B.4).
///
/// <para>
/// Wire format: magic byte <c>0</c>, the 4-byte big-endian schema id, then the Avro binary
/// encoding. The schema id is the registry's id of that exact subject version, so a consumer with
/// any compatible reader schema resolves it.
/// </para>
///
/// <para>
/// Only the Apache.Avro generic API is used (decode JSON with the writer schema, re-encode binary
/// with the same schema): no record is reshaped, so the bytes are exactly the JSON's datum.
/// <c>Confluent.SchemaRegistry.Serdes.Avro</c>'s serializer is not used because it would
/// register/look up the schema from the datum instead of honouring the row's pinned version.
/// </para>
///
/// <para>
/// Schemas are cached per (subject, version): a registered subject version is immutable. Failed
/// lookups are not cached.
/// </para>
/// </summary>
public sealed class SchemaRegistryPayloadEncoder : IPayloadEncoder
{
    private const byte MagicByte = 0;

    // Confluent Schema Registry error codes for "not found" (not transient: retrying the same
    // subject/version will not make it appear until someone registers it).
    private const int SubjectNotFound = 40401;
    private const int VersionNotFound = 40402;
    private const int SchemaNotFound = 40403;

    private readonly ISchemaRegistryClient _registry;
    private readonly ConcurrentDictionary<(string Subject, int Version), WriterSchema> _cache = new();

    public SchemaRegistryPayloadEncoder(ISchemaRegistryClient registry)
    {
        _registry = registry;
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
        var writer = await GetWriterSchemaAsync(SubjectFor(message.Topic), message.SchemaVersion).ConfigureAwait(false);
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

    private async Task<WriterSchema> GetWriterSchemaAsync(string subject, int version)
    {
        if (_cache.TryGetValue((subject, version), out var cached))
        {
            return cached;
        }

        RegisteredSchema registered;
        try
        {
            registered = await _registry.GetRegisteredSchemaAsync(subject, version, ignoreDeletedSchemas: true).ConfigureAwait(false);
        }
        catch (SchemaRegistryException ex) when (ex.ErrorCode is SubjectNotFound or VersionNotFound or SchemaNotFound)
        {
            throw new PayloadEncodingException($"Schema Registry has no subject '{subject}' version {version} (error {ex.ErrorCode}).", isTransient: false, ex);
        }
        catch (SchemaRegistryException ex) when (ex.Status is not (>= HttpStatusCode.InternalServerError))
        {
            throw new PayloadEncodingException($"Schema Registry rejected the lookup of '{subject}' v{version} (HTTP {(int)ex.Status}, error {ex.ErrorCode}).", isTransient: false, ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 5xx, connection refused, DNS, timeouts — and anything unexpected from the client
            // (Confluent.SchemaRegistry 2.16 throws NullReferenceException when MaxRetries = 0 and
            // the registry is unreachable): treated as "registry unavailable", so the batch stops
            // and backs off instead of the exception escaping the publisher.
            throw new PayloadEncodingException($"Schema Registry unavailable looking up '{subject}' v{version}: {ex.GetType().Name}.", isTransient: true, ex);
        }

        if (registered.SchemaType != SchemaType.Avro)
        {
            throw new PayloadEncodingException($"Subject '{subject}' v{version} is {registered.SchemaType}, not Avro.", isTransient: false);
        }

        Schema schema;
        try
        {
            schema = Schema.Parse(registered.SchemaString);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PayloadEncodingException($"Subject '{subject}' v{version} is not a parseable Avro schema.", isTransient: false, ex);
        }

        var writer = new WriterSchema(registered.Id, schema);
        _cache.TryAdd((subject, version), writer);
        return writer;
    }
}

/// <summary>A registry schema id and its parsed Avro schema.</summary>
public sealed record WriterSchema(int Id, Schema Schema);
