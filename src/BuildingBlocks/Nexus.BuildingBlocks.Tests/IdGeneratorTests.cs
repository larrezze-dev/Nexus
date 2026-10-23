using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Application;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// ADR-0009: ids are UUIDv7 minted by the application, from the injected <see cref="TimeProvider"/>.
/// Pure domain test, no Postgres. The <see cref="IIdGenerator"/> under test is the one
/// <c>AddNexusApplication</c> registers (the real, internal <c>UuidV7IdGenerator</c>).
/// </summary>
public sealed class IdGeneratorTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 23, 12, 34, 56, 789, TimeSpan.Zero);

    [Fact(DisplayName = "ADR-0009: IIdGenerator returns RFC 9562 version 7 UUIDs (version nibble 7, variant 10xx)")]
    public void NewId_IsVersion7_WithRfc9562Variant()
    {
        var generator = CreateGenerator(Instant);

        for (var i = 0; i < 100; i++)
        {
            var id = generator.NewId();
            id.Version.ShouldBe(7);

            // Big-endian (RFC 9562) byte order: byte 6 high nibble = version, byte 8 top bits = variant.
            var bytes = id.ToByteArray(bigEndian: true);
            (bytes[6] >> 4).ShouldBe(0x7);
            (bytes[8] & 0xC0).ShouldBe(0x80);
            id.ToString("D")[14].ShouldBe('7');
        }
    }

    [Fact(DisplayName = "ADR-0009: IIdGenerator embeds the injected TimeProvider's instant as the 48-bit Unix-ms timestamp")]
    public void NewId_EmbedsInjectedTimestamp()
    {
        var id = CreateGenerator(Instant).NewId();

        Span<byte> timestamp = stackalloc byte[8];
        id.ToByteArray(bigEndian: true).AsSpan(0, 6).CopyTo(timestamp[2..]);
        BinaryPrimitives.ReadInt64BigEndian(timestamp).ShouldBe(Instant.ToUnixTimeMilliseconds());
    }

    [Fact(DisplayName = "ADR-0009: IIdGenerator never repeats an id")]
    public void NewId_IsUnique()
    {
        var generator = CreateGenerator(Instant);

        var ids = Enumerable.Range(0, 10_000).Select(_ => generator.NewId()).ToHashSet();

        ids.Count.ShouldBe(10_000);
    }

    private static IIdGenerator CreateGenerator(DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddNexusApplication(typeof(IdGeneratorTests).Assembly);
        // The generator holds no disposable state; it stays usable after its container is disposed.
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IIdGenerator>();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
