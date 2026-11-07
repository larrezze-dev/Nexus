using System.Text;
using Confluent.Kafka;

namespace Nexus.BuildingBlocks.Messaging.Dlq;

/// <summary>Builds the DLQ record headers: original headers plus the three <c>nexus-dlq-*</c> ones.</summary>
public static class DlqHeaders
{
    public const string Error = "nexus-dlq-error";
    public const string Consumer = "nexus-dlq-consumer";
    public const string FailedAt = "nexus-dlq-failed-at";
    public const int MaxErrorBytes = 1024;

    public static string TopicFor(string topic) => $"{topic}.DLQ";

    public static Headers Build(Headers? original, Exception error, string consumerGroup, DateTimeOffset failedAt)
    {
        var headers = new Headers();
        if (original is not null)
        {
            foreach (var h in original)
            {
                if (h.Key is Error or Consumer or FailedAt)
                    continue; // never carry stale DLQ headers from a replayed message
                headers.Add(h.Key, h.GetValueBytes());
            }
        }

        headers.Add(Error, TruncateUtf8($"{error.GetType().FullName}: {error.Message}", MaxErrorBytes));
        headers.Add(Consumer, Encoding.UTF8.GetBytes(consumerGroup));
        headers.Add(FailedAt, Encoding.UTF8.GetBytes(
            failedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)));
        return headers;
    }

    /// <summary>Truncates to at most <paramref name="maxBytes"/> UTF-8 bytes without splitting a code point.</summary>
    internal static byte[] TruncateUtf8(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
            return bytes;
        var len = maxBytes;
        while (len > 0 && (bytes[len] & 0xC0) == 0x80)
            len--;
        return bytes[..len];
    }
}
