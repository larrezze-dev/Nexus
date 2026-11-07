using Shouldly;

namespace Nexus.OutboxRelay.Tests;

public sealed class RelayOptionsTests
{
    // Smoke test of the scaffold: the options carry the step 0.7 defaults and the section name.
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
    }
}
