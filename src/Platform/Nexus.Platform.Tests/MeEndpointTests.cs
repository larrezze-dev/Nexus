using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Nexus.Platform.Tests.Integration;
using Shouldly;

namespace Nexus.Platform.Tests;

/// <summary>
/// <c>GET /v1/me</c> against the real host and a real database (step 0.4: the caller's
/// <c>oid</c> is resolved to <c>org.app_user</c> before the endpoint runs, ADR-0006).
/// </summary>
public sealed class MeEndpointTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri Me = new("/v1/me", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public MeEndpointTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    [Fact]
    public async Task Me_endpoint_without_a_token_returns_401()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_endpoint_with_a_token_for_a_different_audience_returns_401()
    {
        using var client = _fixture.CreateClient();
        var token = _fixture.JwtIssuer.IssueToken(oid: Org.UserA.Oid, audience: "api://some-other-service");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_endpoint_with_an_expired_token_returns_401()
    {
        using var client = _fixture.CreateClient();
        var token = _fixture.JwtIssuer.IssueToken(oid: Org.UserA.Oid, lifetime: TimeSpan.FromMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "ADR-0006: /me with the oid of 'unprovisioned' (no app_user) returns 403 user_not_provisioned")]
    public async Task Me_endpoint_for_an_unprovisioned_oid_returns_403_user_not_provisioned()
    {
        using var client = _fixture.CreateClientFor(OrgFixture.UnprovisionedOid);

        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);

        await AssertUserNotProvisionedAsync(response);
    }

    [Fact(DisplayName = "ADR-0006: a token without an oid claim is not provisioned (403)")]
    public async Task Me_endpoint_for_a_token_without_oid_returns_403()
    {
        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fixture.JwtIssuer.IssueToken(oid: null));

        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);

        await AssertUserNotProvisionedAsync(response);
    }

    [Fact(DisplayName = "ADR-0006: /me with the oid of 'usera' returns 200, provisioned=true, its app_user data and language 'de'")]
    public async Task Me_endpoint_for_a_provisioned_user_returns_its_app_user()
    {
        using var client = _fixture.CreateClientFor(Org.UserA.Oid);

        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = (await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken))!.RootElement;
        body.GetProperty("oid").GetString().ShouldBe(Org.UserA.Oid);
        body.GetProperty("provisioned").GetBoolean().ShouldBeTrue();
        body.GetProperty("userId").GetGuid().ShouldBe(Org.UserA.Id);
        body.GetProperty("partyId").GetGuid().ShouldBe(Org.UserA.PartyId);
        body.GetProperty("businessUnitId").GetGuid().ShouldBe(Org.BusinessUnitA.Id);
        body.GetProperty("preferredLanguage").GetString().ShouldBe("de");
        body.GetProperty("permissions").GetArrayLength().ShouldBe(0);
    }

    [Fact(DisplayName = "ADR-0006: preferredLanguage comes from org.app_user, not from the token's locale claim")]
    public async Task Me_endpoint_ignores_the_locale_claim()
    {
        using var client = _fixture.CreateClient();
        var token = _fixture.JwtIssuer.IssueToken(
            oid: Org.Admin.Oid,
            additionalClaims: [new System.Security.Claims.Claim("locale", "en-US")]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = (await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken))!.RootElement;
        body.GetProperty("preferredLanguage").GetString().ShouldBe("de");
        body.GetProperty("businessUnitId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "Public endpoints never resolve identity: /health with an unprovisioned token is 200")]
    public async Task Health_with_an_unprovisioned_token_is_still_200()
    {
        using var client = _fixture.CreateClientFor(OrgFixture.UnprovisionedOid);

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task AssertUserNotProvisionedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = (await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken))!.RootElement;
        body.GetProperty("code").GetString().ShouldBe("user_not_provisioned");
        body.GetProperty("type").GetString().ShouldBe("https://nexus.nussli.ch/problems/forbidden");
        body.GetProperty("status").GetInt32().ShouldBe(403);
    }
}
