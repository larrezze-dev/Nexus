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

    [Fact(DisplayName = "ACC-R10 on legal-entity detail/writes: an ORG_ADMIN of LE-A cannot get, update or deactivate LE-B")]
    public async Task Another_legal_entity_is_forbidden()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var etag = await ETagOfAsync(admin, Org.LegalEntityB.Id);

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        (await userA.GetAsync(LegalEntityUri(Org.LegalEntityB.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var update = await PutAsync(userA, Org.LegalEntityB.Id, etag, new UpdateLegalEntity(Org.LegalEntityB.Code, "EUR", "DE"));
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(update)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");

        // 403, not ORG-R5's 422 (LE-B has active children): the scope check runs first.
        (await DeactivateAsync(userA, Org.LegalEntityB.Id, etag)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ETagOfAsync(admin, Org.LegalEntityB.Id)).ShouldBe(etag); // nothing changed

        // Its own legal entity stays reachable.
        (await userA.GetAsync(LegalEntityUri(Org.LegalEntityA.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "ACC-R10 on legal-entity detail/writes: a GLOBAL admin can get, update and deactivate any legal entity")]
    public async Task Global_admin_covers_every_legal_entity()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewLegalEntity();
        var created = await CreateAsync(admin, request);

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        (await userA.GetAsync(LegalEntityUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await admin.GetAsync(LegalEntityUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await PutAsync(admin, created.Id, created.ETag, new UpdateLegalEntity(request.Code + "-U", "EUR", "DE"));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DeactivateAsync(admin, created.Id, updated.Headers.ETag!.Tag)).StatusCode.ShouldBe(HttpStatusCode.OK);
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

    private static Uri LegalEntityUri(Guid id) => new($"/v1/legal-entities/{id}", UriKind.Relative);

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string etag, UpdateLegalEntity body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, LegalEntityUri(id)) { Content = JsonContent.Create(body) };
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

    private sealed record UpdateLegalEntity(string Code, string CurrencyCode, string CountryCode);

    private sealed record CreatedLegalEntity(Guid Id, string ETag);
}
