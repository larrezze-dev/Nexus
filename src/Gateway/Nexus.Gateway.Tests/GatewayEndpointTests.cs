using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Nexus.Gateway.Tests;

public class GatewayEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_returns_200_without_a_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Proxied_route_without_a_token_returns_401_from_the_gateway_itself()
    {
        // nexus-platform (localhost:5101) is not running for this test: the gateway must
        // reject the request with 401 before ever attempting to proxy it (ADR-0006).
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/platform/v1/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
