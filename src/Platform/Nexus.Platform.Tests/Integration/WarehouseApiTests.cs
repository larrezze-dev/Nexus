using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>/v1/warehouses</c> end to end: real host, real JWT, real PostgreSQL (all Flyway migrations
/// + <see cref="OrgFixture"/>), connecting as <c>svc_platform</c>.
/// </summary>
public sealed class WarehouseApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri Warehouses = new("/v1/warehouses", UriKind.Relative);

    /// <summary>The seeded <c>ORG_ADMIN</c> role (holds <c>ORG_STRUCTURE:UPDATE</c>).</summary>
    private static readonly Guid OrgAdminRoleId = new("00000000-0000-7000-8000-0000000d0002");

    private readonly PlatformApiFixture _fixture;

    public WarehouseApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ORG-R3: a warehouse's businessUnitId must belong to its legalEntityId")]
    public async Task Create_warehouse_with_mismatched_business_unit_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewWarehouse() with { LegalEntityId = Org.LegalEntityA.Id, BusinessUnitId = Org.BusinessUnitB.Id };

        var response = await admin.PostAsJsonAsync(Warehouses, request, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R3");
    }

    [Fact(DisplayName = "ORG-R3: a warehouse whose businessUnitId belongs to its legalEntityId is created")]
    public async Task Create_warehouse_with_matching_business_unit_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewWarehouse() with { LegalEntityId = Org.LegalEntityA.Id, BusinessUnitId = Org.BusinessUnitA.Id };

        var response = await admin.PostAsJsonAsync(Warehouses, request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        body.GetProperty("legalEntityId").GetGuid().ShouldBe(Org.LegalEntityA.Id);
        body.GetProperty("businessUnitId").GetGuid().ShouldBe(Org.BusinessUnitA.Id);
        body.GetProperty("code").GetString().ShouldBe(request.Code);
    }

    [Fact(DisplayName = "ACC-R10 on warehouse detail/writes: an ORG_ADMIN of LE-A cannot get, update or deactivate a warehouse of LE-B")]
    public async Task Warehouse_of_another_legal_entity_is_forbidden()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewWarehouse() with { LegalEntityId = Org.LegalEntityB.Id, LocationId = Org.LocationB });

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        (await userA.GetAsync(WarehouseUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var update = await PutAsync(userA, created.Id, created.ETag, ToUpdate(created.Request) with { Code = created.Request.Code + "-X" });
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(update)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");

        (await DeactivateAsync(userA, created.Id, created.ETag)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Nothing changed: same ETag, still active.
        var current = await admin.GetAsync(WarehouseUri(created.Id), Ct);
        current.Headers.ETag!.Tag.ShouldBe(created.ETag);
        (await ReadAsync(current)).GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Fact(DisplayName = "ACC-R10 on warehouse detail/writes: the LE-B ORG_ADMIN and a GLOBAL admin can get, update and deactivate a warehouse of LE-B")]
    public async Task Warehouse_inside_the_callers_scope_is_allowed()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewWarehouse() with { LegalEntityId = Org.LegalEntityB.Id, LocationId = Org.LocationB });

        using var userB = _fixture.CreateClientFor(Org.UserB.Oid);
        (await userB.GetAsync(WarehouseUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await PutAsync(userB, created.Id, created.ETag, ToUpdate(created.Request) with { Code = created.Request.Code + "-B" });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.GetAsync(WarehouseUri(created.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var deactivated = await DeactivateAsync(admin, created.Id, updated.Headers.ETag!.Tag);
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(deactivated)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact(DisplayName = "ACC-R10 on getWarehouse: a WAREHOUSE-scoped grant covers its own warehouse only")]
    public async Task Warehouse_scoped_reader_sees_only_its_own_warehouse()
    {
        // WhA: WAREHOUSE_OPERATOR (ORG_STRUCTURE:READ) scoped to WH-A only.
        using var whA = _fixture.CreateClientFor(Org.WhA.Oid);

        (await whA.GetAsync(WarehouseUri(Org.WarehouseA), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await whA.GetAsync(WarehouseUri(Org.WarehouseB), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "ACC-R10 on updateWarehouse: a grant narrower than the LE cannot move the warehouse to a business unit it does not cover")]
    public async Task Narrow_scope_updater_cannot_reparent_to_an_uncovered_business_unit()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewWarehouse() with { BusinessUnitId = Org.BusinessUnitA.Id });
        var otherBusinessUnitInLeA = await CreateBusinessUnitAsync(admin, Org.LegalEntityA.Id);

        // A fresh user holding ORG_ADMIN (ORG_STRUCTURE:UPDATE) on this one warehouse only.
        var oid = await CreateWarehouseScopedOrgAdminAsync(admin, created.Id);
        using var narrow = _fixture.CreateClientFor(oid);

        var reparent = await PutAsync(narrow, created.Id, created.ETag, ToUpdate(created.Request) with { BusinessUnitId = otherBusinessUnitInLeA });
        reparent.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var sameParent = await PutAsync(narrow, created.Id, created.ETag, ToUpdate(created.Request) with { Code = created.Request.Code + "-N" });
        sameParent.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(sameParent)).GetProperty("businessUnitId").GetGuid().ShouldBe(Org.BusinessUnitA.Id);
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>A valid, unique create request (own code), in LE-A/LOC-A, no business unit set.</summary>
    private CreateWarehouse NewWarehouse()
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return new CreateWarehouse(
            Id: Guid.CreateVersion7(),
            LocationId: Org.LocationA,
            LegalEntityId: Org.LegalEntityA.Id,
            BusinessUnitId: null,
            Code: "WH-TEST-" + tag,
            CaptionI18n: new Dictionary<string, string> { ["de"] = "Lager " + tag, ["en"] = "Warehouse " + tag });
    }

    private static Uri WarehouseUri(Guid id) => new($"/v1/warehouses/{id}", UriKind.Relative);

    private static UpdateWarehouse ToUpdate(CreateWarehouse request) =>
        new(request.LocationId, request.BusinessUnitId, request.Code, request.CaptionI18n);

    private static async Task<CreatedWarehouse> CreateAsync(HttpClient client, CreateWarehouse request)
    {
        var response = await client.PostAsJsonAsync(Warehouses, request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return new CreatedWarehouse((await ReadAsync(response)).GetProperty("id").GetGuid(), response.Headers.ETag!.Tag, request);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string etag, UpdateWarehouse body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, WarehouseUri(id)) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid id, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/warehouses/{id}/deactivate", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<Guid> CreateBusinessUnitAsync(HttpClient admin, Guid legalEntityId)
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var response = await admin.PostAsJsonAsync(
            new Uri("/v1/business-units", UriKind.Relative),
            new
            {
                legalName = "Test Business Unit " + tag,
                legalEntityId,
                code = "BU-TEST-" + tag,
                captionI18n = new Dictionary<string, string> { ["de"] = "BU " + tag },
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A fresh user plus an <c>ORG_ADMIN</c> assignment scoped to <paramref name="warehouseId"/>
    /// only (granted by the GLOBAL admin); returns the user's oid for its JWT.
    /// </summary>
    private static async Task<string> CreateWarehouseScopedOrgAdminAsync(HttpClient admin, Guid warehouseId)
    {
        var tag = Guid.NewGuid().ToString("N");
        var oid = "oid-" + tag;
        var user = await admin.PostAsJsonAsync(
            new Uri("/v1/users", UriKind.Relative),
            new { oid, fullName = "Narrow Admin " + tag, email = $"narrow.{tag}@test.nexus.local", preferredLanguage = "en" },
            Ct);
        user.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await ReadAsync(user)).GetProperty("id").GetGuid();

        var grant = await admin.PostAsJsonAsync(
            new Uri("/v1/role-assignments", UriKind.Relative),
            new { userId, roleId = OrgAdminRoleId, scopeType = "WAREHOUSE", scopeEntityId = warehouseId, validFrom = DateTimeOffset.UtcNow },
            Ct);
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);
        return oid;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private sealed record UpdateWarehouse(
        Guid LocationId,
        Guid? BusinessUnitId,
        string Code,
        IReadOnlyDictionary<string, string> CaptionI18n);

    private sealed record CreatedWarehouse(Guid Id, string ETag, CreateWarehouse Request);

    private sealed record CreateWarehouse(
        Guid? Id,
        Guid LocationId,
        Guid LegalEntityId,
        Guid? BusinessUnitId,
        string Code,
        IReadOnlyDictionary<string, string> CaptionI18n);
}
