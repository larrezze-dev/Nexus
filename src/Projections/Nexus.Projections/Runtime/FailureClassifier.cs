using System.Net;
using System.Net.Sockets;
using ClickHouse.Client;
using ClickHouse.Client.Copy;
using Confluent.SchemaRegistry;

namespace Nexus.Projections.Runtime;

/// <summary>
/// Failure classes of the projection runtime.
/// <list type="bullet">
///   <item>Schema Registry: an INFRASTRUCTURE failure (unreachable, timing out, 5xx, 408, 429, and — decision
///   D3 — credentials refused 401/403/407) vs a deterministic failure of the record itself (poison bytes,
///   unknown schema id, invalid Avro, broken envelope, <c>Map</c> throwing). Luciano's 0.7 decision
///   "transient ≠ FAILED": an infrastructure failure never sends a record to the DLQ and never commits;
///   the consumer pauses and retries.</item>
///   <item>ClickHouse insert: REJECTED (the batch itself is refused: decision D1) vs UNAVAILABLE. Both block
///   the projection; only the metric kind and the log differ.</item>
/// </list>
/// </summary>
internal static class FailureClassifier
{
    /// <summary>
    /// True when <paramref name="exception"/> (or anything in its inner chain) says the registry could not
    /// be reached or answered with a server-side / throttling status. "Schema not found" (404 / 40403) and
    /// other 4xx answers are deterministic: the bytes reference a schema the registry does not have.
    /// </summary>
    public static bool IsTransientSchemaRegistryFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SchemaRegistryException sr:
                    return IsTransientStatus(sr.Status);
                case HttpRequestException http:
                    return http.StatusCode is null || IsTransientStatus(http.StatusCode.Value);
                case TaskCanceledException or TimeoutException or SocketException:
                    // HttpClient timeout (not our token: the caller filters host cancellation first).
                    return true;
                case AggregateException aggregate when aggregate.InnerExceptions.Any(IsTransientSchemaRegistryFailure):
                    return true;
            }
        }

        return false;
    }

    private static bool IsTransientStatus(HttpStatusCode status) =>
        (int)status >= 500
        || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            // D3: the registry refusing OUR credentials (or a proxy's) is an infrastructure/config problem,
            // never a property of the record: pause and alert instead of dead-lettering every record.
            or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.ProxyAuthenticationRequired;

    /// <summary>
    /// ClickHouse error codes meaning "these rows do not fit the table": type, parse and value errors.
    /// NO_SUCH_COLUMN_IN_TABLE (16), UNKNOWN_TABLE (60) and UNKNOWN_DATABASE (81) are NOT here: they mean
    /// the schema is not deployed yet (a migration pending), so they count as unavailable, not a bad batch.
    /// </summary>
    internal static readonly IReadOnlySet<int> RejectionCodes = new HashSet<int>
    {
        6,   // CANNOT_PARSE_TEXT
        26,  // CANNOT_PARSE_QUOTED_STRING
        27,  // CANNOT_PARSE_INPUT_ASSERTION_FAILED
        32,  // ATTEMPT_TO_READ_AFTER_EOF
        33,  // CANNOT_READ_ALL_DATA
        38,  // CANNOT_PARSE_DATE
        41,  // CANNOT_PARSE_DATETIME
        43,  // ILLEGAL_TYPE_OF_ARGUMENT
        53,  // TYPE_MISMATCH
        69,  // ARGUMENT_OUT_OF_BOUND
        70,  // CANNOT_CONVERT_TYPE
        72,  // CANNOT_PARSE_NUMBER
        117, // INCORRECT_DATA
        349, // CANNOT_INSERT_NULL_IN_ORDINARY_COLUMN
        376, // CANNOT_PARSE_UUID
    };

    /// <summary>
    /// True when an insert failed because the BATCH is refused (decision D1): a ClickHouse server error with
    /// a type/parse/value code (<see cref="RejectionCodes"/>), or a client-side RowBinary serialization error
    /// (<see cref="ClickHouseBulkCopySerializationException"/>, or <see cref="ProjectionRowSerializationException"/>
    /// raised by the sink around the bulk-copy write only). Anything else (network, timeout, 5xx, missing
    /// table or column) is "unavailable". <paramref name="code"/> is the ClickHouse error code found in the
    /// chain, or 0.
    /// </summary>
    public static bool IsInsertRejection(Exception exception, out int code)
    {
        ArgumentNullException.ThrowIfNull(exception);
        code = 0;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case ClickHouseServerException server:
                    code = server.ErrorCode;
                    return RejectionCodes.Contains(server.ErrorCode);
                case ClickHouseBulkCopySerializationException or ProjectionRowSerializationException:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A log-safe description of an insert failure: the exception types of the chain and the ClickHouse error
    /// code, never a message (ClickHouse messages quote the offending values).
    /// </summary>
    public static string SafeDescription(Exception exception, int code)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var types = new List<string>();
        for (var current = exception; current is not null && types.Count < 5; current = current.InnerException)
        {
            types.Add(current.GetType().Name);
        }

        return code == 0 ? string.Join(" -> ", types) : $"{string.Join(" -> ", types)}, code {code}";
    }
}
