using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nexus.BuildingBlocks.Web.Observability;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.Observability;

/// <summary>
/// No real OTLP collector runs in this test (conventions-backend.md §8 / architecture.md §6);
/// this only verifies the host starts and stops cleanly with the OTel pipeline wired, which is
/// exactly what matters here - the exporter must never crash the host when nothing is
/// listening on the configured endpoint.
/// </summary>
public sealed class TelemetryServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddNexusTelemetry_Starts_And_Stops_The_Host_Without_A_Collector()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Deliberately a port nothing listens on: the exporter must not throw here.
                ["Nexus:Telemetry:OtlpEndpoint"] = "http://localhost:14317",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexusTelemetry(configuration, serviceName: "web-tests");
        using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.ShouldNotBeEmpty();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void NexusTelemetryOptions_Defaults_OtlpEndpoint_To_Local_4317()
    {
        new NexusTelemetryOptions().OtlpEndpoint.ShouldBe("http://localhost:4317");
    }
}
