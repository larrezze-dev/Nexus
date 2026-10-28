using System.Net;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebAppFixture<Program>>
{
    private readonly WebAppFixture<Program> _fixture;

    public HealthEndpointTests(WebAppFixture<Program> fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Health_endpoint_returns_200_without_a_token()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
