using Shouldly;

namespace Nexus.OutboxRelay.Tests;

public sealed class RelayOptionsTests
{
    // The options carry the step 0.7 defaults (and the [+] ones) and the section name.
    [Fact]
    public void Defaults_MatchStep07Values()
    {
        var options = new RelayOptions();

        RelayOptions.SectionName.ShouldBe("Nexus:Relay");
        options.BatchSize.ShouldBe(500);
        options.PollIntervalMs.ShouldBe(500);
        options.LeaderRetrySeconds.ShouldBe(5);
        options.MaxAttempts.ShouldBe(10);
        options.PurgeRetentionDays.ShouldBe(7);
        options.PurgeMaxChunksPerPass.ShouldBe(10);
        options.ContractsDirectory.ShouldBeEmpty();
        typeof(RelayOptions).GetProperty("MaxHeldAggregates").ShouldBeNull("holds are per topic, with no cap (decision of Luciano, 2026-11-08)");
        options.ResolveContractsDirectory().ShouldBe(Path.Combine(AppContext.BaseDirectory, "contracts", "events"));
    }
}
