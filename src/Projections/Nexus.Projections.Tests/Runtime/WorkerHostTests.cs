using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Nexus.Projections.Tests.Runtime.Infrastructure;
using Shouldly;

namespace Nexus.Projections.Tests.Runtime;

/// <summary>The real worker host (<c>nexus-projections run</c>) built by the CLI.</summary>
[Collection(RuntimeTestGroup.Name)]
public sealed class WorkerHostTests(RuntimeEnvironment env)
{
    [Fact(DisplayName = "Health port: ASPNETCORE_URLS wins over the built-in default and /health answers there")]
    public async Task Aspnetcore_urls_wins()
    {
        var port = FreePort();
        var previous = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://127.0.0.1:{port}");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<int>? run = null;
        try
        {
            // No projection: the host only applies the migrations and serves /health.
            run = Task.Run(() => ProjectionsCli.RunAsync(
                ["run"], _ => { }, c => c.AddInMemoryCollection(RuntimeHarness.BaseConfiguration(env)), cts.Token));

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            HttpStatusCode? status = null;
            await Eventually.Until(async () =>
            {
                try
                {
                    status = (await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health"))).StatusCode;
                    return true;
                }
                catch (HttpRequestException)
                {
                    return run.IsCompleted; // stop polling if the host died
                }
            }, TimeSpan.FromSeconds(30));

            run.IsCompleted.ShouldBeFalse("the host must still be running");
            status.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", previous);
            await cts.CancelAsync();
            if (run is not null)
            {
                (await run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).ShouldBe(ProjectionsCli.ExitOk);
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
