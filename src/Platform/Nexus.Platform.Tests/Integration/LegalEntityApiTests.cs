using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>/v1/legal-entities</c> end to end: real host, real JWT, real PostgreSQL (all Flyway
/// migrations + <see cref="OrgFixture"/>), connecting as <c>svc_platform</c>.
/// </summary>
public sealed class LegalEntityApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri LegalEntities = new("/v1/legal-entities", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public LegalEntityApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ORG-R5: a legal entity with an active business unit cannot be deactivated")]
    public async Task Deactivate_legal_entity_with_active_business_unit_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var etag = await ETagOfAsync(admin, Org.LegalEntityA.Id);

        var response = await DeactivateAsync(admin, Org.LegalEntityA.Id, etag);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R5");
        (await ETagOfAsync(admin, Org.LegalEntityA.Id)).ShouldBe(etag); // nothing changed
    }

    [Fact(DisplayName = "ORG-R5: a legal entity with no active business unit or warehouse can be deactivated")]
    public async Task Deactivate_legal_entity_with_no_children_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewLegalEntity());

        var response = await DeactivateAsync(admin, created.Id, created.ETag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>A valid, unique create request (own code), under the seeded group, in CHF/CH.</summary>
    private CreateLegalEntity NewLegalEntity()
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return new CreateLegalEntity(
            Id: Guid.CreateVersion7(),
            LegalName: "Test Legal Entity " + tag,
            DisplayName: null,
            GroupId: Org.GroupId,
            Code: "LE-TEST-" + tag,
            CurrencyCode: "CHF",
            CountryCode: "CH");
    }

    private static async Task<CreatedLegalEntity> CreateAsync(HttpClient client, CreateLegalEntity request)
    {
        var response = await client.PostAsJsonAsync(LegalEntities, request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        return new CreatedLegalEntity(body.GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    private static async Task<string> ETagOfAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync(new Uri($"/v1/legal-entities/{id}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response.Headers.ETag!.Tag;
    }

    private static async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid id, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/legal-entities/{id}/deactivate", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private sealed record CreateLegalEntity(
        Guid? Id,
        string LegalName,
        string? DisplayName,
        Guid GroupId,
        string Code,
        string CurrencyCode,
        string CountryCode);

    private sealed record CreatedLegalEntity(Guid Id, string ETag);
}
