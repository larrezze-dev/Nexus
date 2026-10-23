using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Application.Tests;

/// <summary>
/// Verifies the default <see cref="IIdGenerator"/> produces real UUIDv7 values (ADR-0009),
/// driven by an injected <see cref="TimeProvider"/> rather than wall-clock time.
/// </summary>
public sealed class IdGeneratorTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public void NewId_ProducesAVersion7Guid()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)));
        services.AddNexusApplication(typeof(IdGeneratorTests).Assembly);
        using var provider = services.BuildServiceProvider();
        var idGenerator = provider.GetRequiredService<IIdGenerator>();

        var id = idGenerator.NewId();

        // RFC 9562 layout as rendered by Guid's "D" format (xxxxxxxx-xxxx-Vxxx-Nxxx-xxxxxxxxxxxx):
        // the leading hex digit of the third group is the version, and of the fourth group the
        // variant (top two bits fixed to "10", i.e. one of 8/9/a/b).
        var groups = id.ToString().Split('-');
        groups[2][0].ShouldBe('7');
        "89ab".ShouldContain(char.ToLowerInvariant(groups[3][0]).ToString());
    }

    [Fact]
    public void NewId_RespectsTheInjectedTimeProvider()
    {
        var earlier = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Guid MakeId(DateTimeOffset instant)
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(instant));
            services.AddNexusApplication(typeof(IdGeneratorTests).Assembly);
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IIdGenerator>().NewId();
        }

        var earlierId = MakeId(earlier);
        var laterId = MakeId(later);

        // UUIDv7 is time-ordered: an id minted for an earlier instant sorts before one minted
        // for a later instant.
        string.CompareOrdinal(earlierId.ToString(), laterId.ToString()).ShouldBeLessThan(0);
    }
}
