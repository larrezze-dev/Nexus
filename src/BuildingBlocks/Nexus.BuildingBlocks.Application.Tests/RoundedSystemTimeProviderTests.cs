using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Application.Tests;

/// <summary>
/// <see cref="RoundedSystemTimeProvider"/> must only ever hand out whole-microsecond instants,
/// because event instants are Avro <c>timestamp-micros</c> and the outbox serializer rejects
/// anything finer. A single call proves little (the raw clock is sometimes aligned by chance),
/// so the property is checked across many calls.
/// </summary>
public sealed class RoundedSystemTimeProviderTests
{
    [Fact]
    public void GetUtcNow_IsAlwaysMicrosecondAligned()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var now = RoundedSystemTimeProvider.Instance.GetUtcNow();
            (now.Ticks % TimeSpan.TicksPerMicrosecond).ShouldBe(0L, $"iteration {i}, ticks={now.Ticks}");
        }
    }

    [Fact]
    public void GetUtcNow_IsUtc_AndNeverAheadOfTheSystemClock()
    {
        var now = RoundedSystemTimeProvider.Instance.GetUtcNow();
        var after = TimeProvider.System.GetUtcNow();

        now.Offset.ShouldBe(TimeSpan.Zero);
        now.ShouldBeLessThanOrEqualTo(after);
        (after - now).ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AddNexusApplication_DefaultsToRoundedSystemTimeProvider()
    {
        var services = new ServiceCollection();

        services.AddNexusApplication();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(RoundedSystemTimeProvider.Instance);
    }

    [Fact]
    public void AddNexusApplication_KeepsAnAlreadyRegisteredTimeProvider()
    {
        var custom = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(custom);

        services.AddNexusApplication();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(custom);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
