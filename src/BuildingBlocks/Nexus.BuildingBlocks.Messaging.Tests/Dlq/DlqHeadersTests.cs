using System.Text;
using Confluent.Kafka;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Shouldly;

namespace Nexus.BuildingBlocks.Messaging.Tests.Dlq;

public class DlqHeadersTests
{
    private static string Get(Headers h, string key) => Encoding.UTF8.GetString(h.GetLastBytes(key));

    [Fact]
    public void Adds_three_headers_and_keeps_originals()
    {
        var original = new Headers { { "event-id", Encoding.UTF8.GetBytes("abc") } };
        var at = new DateTimeOffset(2026, 11, 7, 10, 30, 0, TimeSpan.FromHours(2));

        var h = DlqHeaders.Build(original, new InvalidOperationException("bad"), "ops.reserve", at);

        Get(h, "event-id").ShouldBe("abc");
        Get(h, DlqHeaders.Error).ShouldBe("System.InvalidOperationException: bad");
        Get(h, DlqHeaders.Consumer).ShouldBe("ops.reserve");
        Get(h, DlqHeaders.FailedAt).ShouldBe("2026-11-07T08:30:00.000Z");
        DlqHeaders.TopicFor("a.b.v1").ShouldBe("a.b.v1.DLQ");
    }

    [Fact]
    public void Error_is_truncated_to_1KB_on_a_codepoint_boundary()
    {
        var h = DlqHeaders.Build(null, new InvalidOperationException(new string('é', 2000)), "g", DateTimeOffset.UnixEpoch);

        var bytes = h.GetLastBytes(DlqHeaders.Error);
        bytes.Length.ShouldBeLessThanOrEqualTo(1024);
        Should.NotThrow(() => new UTF8Encoding(false, true).GetString(bytes));
    }

    [Fact]
    public void Stale_dlq_headers_are_replaced()
    {
        var original = new Headers { { DlqHeaders.Consumer, Encoding.UTF8.GetBytes("old") } };
        var h = DlqHeaders.Build(original, new InvalidOperationException("x"), "new", DateTimeOffset.UnixEpoch);
        h.Count(x => x.Key == DlqHeaders.Consumer).ShouldBe(1);
        Get(h, DlqHeaders.Consumer).ShouldBe("new");
    }
}
