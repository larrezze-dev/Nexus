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

    [Fact]
    public async Task A_valid_inbound_correlation_id_is_echoed_back_unchanged()
    {
        using var client = _factory.CreateClient();
        var correlationId = Guid.CreateVersion7().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").ShouldHaveSingleItem().ShouldBe(correlationId);
    }

    [Fact]
    public async Task An_inbound_correlation_id_that_is_not_a_valid_guid_is_never_reflected()
    {
        // Found live during review: the gateway used to echo back any text verbatim, both to
        // the response header and to the request it forwarded downstream (log/response
        // injection risk). A malformed value must be discarded and replaced, same as a
        // missing header -- never passed through.
        using var client = _factory.CreateClient();
        const string malformed = "<script>alert(1)</script> not-a-guid";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", malformed);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var echoed = response.Headers.GetValues("X-Correlation-Id").ShouldHaveSingleItem();
        echoed.ShouldNotBe(malformed);
        Guid.TryParse(echoed, out var parsed).ShouldBeTrue();
        parsed.ShouldNotBe(Guid.Empty);
    }
}
