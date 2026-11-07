using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

public sealed class WiringTests
{
    [Fact(DisplayName = "AddOutboxRelay: every component resolves and the relay worker is the hosted service")]
    public void AddOutboxRelay_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(TimeProvider.System);
        services.Configure<RelayOptions>(o => o.ConnectionString = "Host=localhost;Username=svc_relay");
        services.AddOutboxRelay();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetServices<IHostedService>().ShouldHaveSingleItem().ShouldBeOfType<RelayWorker>();
    }
}
