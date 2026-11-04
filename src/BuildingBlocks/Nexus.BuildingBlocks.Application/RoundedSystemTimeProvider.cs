namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// The system clock with <see cref="GetUtcNow"/> truncated down to a whole microsecond
/// (a multiple of 10 ticks). Use this instead of <see cref="TimeProvider.System"/>.
/// </summary>
/// <remarks>
/// <para>
/// Domain event instants are serialized with Avro's <c>timestamp-micros</c> logical type
/// (conventions-events.md), which cannot represent sub-microsecond precision.
/// <c>AvroPayloadSerializer</c> deliberately rejects a value finer than its logical type instead
/// of silently rounding it. On Linux the raw system clock resolves below one microsecond, so
/// <see cref="TimeProvider.System"/> returns a value whose <c>Ticks</c> is not a multiple of 10
/// about 90% of the time, and any event carrying that "now" fails the outbox write. Aligning
/// "now" at its source fixes every caller at once.
/// </para>
/// <para>
/// Truncation (not rounding) keeps <see cref="GetUtcNow"/> monotonic with the underlying clock
/// and never produces an instant in the future. Only <see cref="GetUtcNow"/> is overridden:
/// <see cref="TimeProvider.GetTimestamp"/>, <see cref="TimeProvider.TimestampFrequency"/>,
/// <see cref="TimeProvider.LocalTimeZone"/> and <see cref="TimeProvider.CreateTimer"/> keep the
/// base implementation, so elapsed-time measurements and timers stay at full resolution.
/// </para>
/// <para>
/// Stateless, so <see cref="Instance"/> is safe to share as a singleton.
/// </para>
/// </remarks>
public sealed class RoundedSystemTimeProvider : TimeProvider
{
    /// <summary>Shared instance; the type holds no state.</summary>
    public static readonly TimeProvider Instance = new RoundedSystemTimeProvider();

    private RoundedSystemTimeProvider()
    {
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        var now = TimeProvider.System.GetUtcNow();
        var truncated = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(truncated, TimeSpan.Zero);
    }
}
