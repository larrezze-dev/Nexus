using System.Net;
using System.Net.Sockets;
using Confluent.SchemaRegistry;

namespace Nexus.Projections.Runtime;

/// <summary>
/// Tells a TRANSIENT infrastructure failure of the Schema Registry (unreachable, timing out, 5xx, 429)
/// from a deterministic failure of the record itself (poison bytes, unknown schema id, invalid Avro,
/// broken envelope, <c>Map</c> throwing). Luciano's 0.7 decision "transient ≠ FAILED": a transient
/// failure never sends a record to the DLQ and never commits; the consumer pauses and retries.
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
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
}
