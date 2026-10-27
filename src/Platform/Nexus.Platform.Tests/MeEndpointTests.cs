using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests;

public sealed class MeEndpointTests : IClassFixture<WebAppFixture<Program>>
{
    private const string Oid = "00000000-0000-7000-8000-00000000a002";

    private readonly WebAppFixture<Program> _fixture;

    public MeEndpointTests(WebAppFixture<Program> fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Me_endpoint_without_a_token_returns_401()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_endpoint_with_a_token_for_a_different_audience_returns_401()
    {
        using var client = _fixture.CreateClient();
        var token = _fixture.JwtIssuer.IssueToken(audience: "api://some-other-service");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(new Uri("/v1/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_endpoint_with_an_expired_token_returns_401()
    {
        using var client = _fixture.CreateClient();
        var token = _fixture.JwtIssuer.IssueToken(lifetime: TimeSpan.FromMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(new Uri("/v1/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_endpoint_with_a_valid_token_returns_200_with_its_oid()
    {
        using var client = _fixture.CreateClient();
        var token = _fixture.JwtIssuer.IssueToken(oid: Oid);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(new Uri("/v1/me", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        body!.RootElement.GetProperty("oid").GetString().ShouldBe(Oid);
        body.RootElement.GetProperty("provisioned").GetBoolean().ShouldBeFalse();
        body.RootElement.GetProperty("permissions").GetArrayLength().ShouldBe(0);
    }
}
