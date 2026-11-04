using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>/v1/business-units</c> end to end: real host, real JWT, real PostgreSQL (all Flyway
/// migrations + <see cref="OrgFixture"/>), connecting as <c>svc_platform</c>.
/// </summary>
public sealed class BusinessUnitApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri BusinessUnits = new("/v1/business-units", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public BusinessUnitApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ACC-R10 on business-unit detail/writes: an ORG_ADMIN of LE-A cannot get, update or deactivate a business unit of LE-B")]
    public async Task Business_unit_of_another_legal_entity_is_forbidden()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewBusinessUnit(Org.LegalEntityB.Id));

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        (await userA.GetAsync(BusinessUnitUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var update = await PutAsync(userA, created.Id, created.ETag, ToUpdate(created.Request) with { Code = created.Request.Code + "-X" });
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(update)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");

        (await DeactivateAsync(userA, created.Id, created.ETag)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Nothing changed: same ETag, still active.
        var current = await admin.GetAsync(BusinessUnitUri(created.Id), Ct);
        current.Headers.ETag!.Tag.ShouldBe(created.ETag);
        (await ReadAsync(current)).GetProperty("isActive").GetBoolean().ShouldBeTrue();

        // Its own legal entity's business unit stays reachable.
        (await userA.GetAsync(BusinessUnitUri(Org.BusinessUnitA.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "ACC-R10 on business-unit detail/writes: the LE-B ORG_ADMIN and a GLOBAL admin can get, update and deactivate a business unit of LE-B")]
    public async Task Business_unit_inside_the_callers_scope_is_allowed()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewBusinessUnit(Org.LegalEntityB.Id));

        using var userB = _fixture.CreateClientFor(Org.UserB.Oid);
        (await userB.GetAsync(BusinessUnitUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await PutAsync(userB, created.Id, created.ETag, ToUpdate(created.Request) with { Code = created.Request.Code + "-B" });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.GetAsync(BusinessUnitUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var deactivated = await DeactivateAsync(admin, created.Id, updated.Headers.ETag!.Tag);
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(deactivated)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>A valid, unique create request (own code) in <paramref name="legalEntityId"/>.</summary>
    private static CreateBusinessUnit NewBusinessUnit(Guid legalEntityId)
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return new CreateBusinessUnit(
            Id: Guid.CreateVersion7(),
            LegalName: "Test Business Unit " + tag,
            LegalEntityId: legalEntityId,
            Code: "BU-TEST-" + tag,
            CaptionI18n: new Dictionary<string, string> { ["de"] = "Einheit " + tag, ["en"] = "Unit " + tag },
            CostCenterCode: null);
    }

    private static Uri BusinessUnitUri(Guid id) => new($"/v1/business-units/{id}", UriKind.Relative);

    private static UpdateBusinessUnit ToUpdate(CreateBusinessUnit request) =>
        new(request.Code, request.CaptionI18n, request.CostCenterCode);

    private static async Task<CreatedBusinessUnit> CreateAsync(HttpClient client, CreateBusinessUnit request)
    {
        var response = await client.PostAsJsonAsync(BusinessUnits, request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return new CreatedBusinessUnit((await ReadAsync(response)).GetProperty("id").GetGuid(), response.Headers.ETag!.Tag, request);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string etag, UpdateBusinessUnit body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BusinessUnitUri(id)) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid id, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/business-units/{id}/deactivate", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private sealed record CreateBusinessUnit(
        Guid? Id,
        string LegalName,
        Guid LegalEntityId,
        string Code,
        IReadOnlyDictionary<string, string> CaptionI18n,
        string? CostCenterCode);

    private sealed record UpdateBusinessUnit(string Code, IReadOnlyDictionary<string, string> CaptionI18n, string? CostCenterCode);

    private sealed record CreatedBusinessUnit(Guid Id, string ETag, CreateBusinessUnit Request);
}
