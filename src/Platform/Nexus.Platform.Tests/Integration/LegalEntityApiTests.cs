using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Application;
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

    private static readonly Guid OrgAdminRoleId = new("00000000-0000-7000-8000-0000000d0002");

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

    [Fact(DisplayName = "ACC-R4/ACC-R10 (intentional): a WAREHOUSE-scoped grant does not cover its own warehouse's parent legal entity (403)")]
    public async Task Warehouse_scoped_grant_does_not_cover_its_parent_legal_entity()
    {
        // INTENTIONAL, do not "fix": coverage only flows downwards (GLOBAL ⊃ GROUP ⊃ LEGAL_ENTITY
        // ⊃ BUSINESS_UNIT ⊃ WAREHOUSE). WhA holds ORG_STRUCTURE:READ on WH-A only, so it can read
        // WH-A but not LE-A, even though LE-A owns WH-A. If this ever turns into a 200, a narrow
        // grant has started to leak upwards: that needs an explicit decision, not a silent change.
        using var whA = _fixture.CreateClientFor(Org.WhA.Oid);
        (await whA.GetAsync(new Uri($"/v1/warehouses/{Org.WarehouseA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _fixture.Database.ScalarAsync<Guid>("SELECT legal_entity_id FROM org.warehouse WHERE id = $1", Org.WarehouseA))
            .ShouldBe(Org.LegalEntityA.Id);

        var response = await whA.GetAsync(LegalEntityUri(Org.LegalEntityA.Id), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");
    }

    [Fact(DisplayName = "ACC-R10 on listLegalEntities: rows are filtered like getLegalEntity (LE-A ORG_ADMIN: only LE-A; GLOBAL: all)")]
    public async Task List_legal_entities_is_filtered_by_the_callers_coverage()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var extra = await CreateAsync(admin, NewLegalEntity());

        var all = await ListAsync(admin, "pageSize=200");
        all.Ids.ShouldContain(Org.LegalEntityA.Id);
        all.Ids.ShouldContain(Org.LegalEntityB.Id);
        all.Ids.ShouldContain(extra.Id);
        all.Total.ShouldBe(await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM org.legal_entity"));

        var ofA = await ListAsync(userA, "pageSize=200");
        ofA.Ids.ShouldBe([Org.LegalEntityA.Id]);
        ofA.Total.ShouldBe(1);
    }

    [Fact(DisplayName = "ACC-R10 on listLegalEntities: pagination and total apply to the coverage-filtered rows, not the unfiltered ones")]
    public async Task List_legal_entities_paginates_and_counts_after_coverage_filtering()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        await CreateAsync(admin, NewLegalEntity());
        await CreateAsync(admin, NewLegalEntity());

        // A fresh ORG_ADMIN of LE-A and LE-B only: 2 visible out of at least 4.
        var oid = await CreateOrgAdminOfAsync(admin, Org.LegalEntityA.Id, Org.LegalEntityB.Id);
        (await ListAsync(admin, "pageSize=1")).Total.ShouldBeGreaterThanOrEqualTo(4);

        using var both = _fixture.CreateClientFor(oid);
        var page1 = await ListAsync(both, "pageSize=1&page=1");
        var page2 = await ListAsync(both, "pageSize=1&page=2");
        var page3 = await ListAsync(both, "pageSize=1&page=3");

        page1.Total.ShouldBe(2);
        page2.Total.ShouldBe(2);
        page3.Total.ShouldBe(2);
        page1.Ids.Count.ShouldBe(1);
        page2.Ids.Count.ShouldBe(1);
        page3.Ids.ShouldBeEmpty();
        page1.Ids.Concat(page2.Ids).ShouldBe([Org.LegalEntityA.Id, Org.LegalEntityB.Id], ignoreOrder: true);
    }

    [Fact(DisplayName = "UI-LE-LIST: list items include legalName and displayName from the party")]
    public async Task List_legal_entities_items_include_party_names()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewLegalEntity() with { DisplayName = "Shown " + Guid.NewGuid().ToString("N")[..8] };
        var created = await CreateAsync(admin, request);
        var plain = await CreateAsync(admin, NewLegalEntity());

        var items = await ListItemsAsync(admin, "pageSize=200");

        var item = items.Single(e => e.GetProperty("id").GetGuid() == created.Id);
        item.GetProperty("legalName").GetString().ShouldBe(request.LegalName);
        item.GetProperty("displayName").GetString().ShouldBe(request.DisplayName);
        items.Single(e => e.GetProperty("id").GetGuid() == plain.Id)
            .GetProperty("displayName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "UI-LE-LIST: q matches code or legal name, case-insensitively")]
    public async Task List_legal_entities_q_matches_code_or_legal_name_case_insensitively()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var byCode = await CreateAsync(admin, NewLegalEntity() with { Code = "ZQC-" + tag });
        var byName = await CreateAsync(admin, NewLegalEntity() with { LegalName = "Zqn Holding " + tag });
        var other = await CreateAsync(admin, NewLegalEntity());

        var codeHit = await ListAsync(admin, "pageSize=200&q=" + ("zqc-" + tag).ToLowerInvariant());
        codeHit.Ids.ShouldBe([byCode.Id]);

        var nameHit = await ListAsync(admin, "pageSize=200&q=" + ("ZQN HOLDING " + tag).ToUpperInvariant());
        nameHit.Ids.ShouldBe([byName.Id]);

        var none = await ListAsync(admin, "pageSize=200&q=zq-no-such-" + tag);
        none.Ids.ShouldBeEmpty();
        none.Total.ShouldBe(0);
        (await ListAsync(admin, "pageSize=200")).Ids.ShouldContain(other.Id);
    }

    [Fact(DisplayName = "UI-LE-LIST: isActive=false returns only inactive legal entities and isActive=true only active ones")]
    public async Task List_legal_entities_filters_by_is_active()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var toDeactivate = await CreateAsync(admin, NewLegalEntity());
        var active = await CreateAsync(admin, NewLegalEntity());
        (await DeactivateAsync(admin, toDeactivate.Id, toDeactivate.ETag)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var inactive = await ListItemsAsync(admin, "pageSize=200&isActive=false");
        inactive.ShouldNotBeEmpty();
        inactive.ShouldAllBe(e => !e.GetProperty("isActive").GetBoolean());
        inactive.Select(e => e.GetProperty("id").GetGuid()).ShouldContain(toDeactivate.Id);
        inactive.Select(e => e.GetProperty("id").GetGuid()).ShouldNotContain(active.Id);

        var actives = await ListItemsAsync(admin, "pageSize=200&isActive=true");
        actives.ShouldAllBe(e => e.GetProperty("isActive").GetBoolean());
        actives.Select(e => e.GetProperty("id").GetGuid()).ShouldContain(active.Id);
        actives.Select(e => e.GetProperty("id").GetGuid()).ShouldNotContain(toDeactivate.Id);
    }

    [Fact(DisplayName = "UI-LE-LIST: sort=legalName orders by legal name ascending and -legalName descending")]
    public async Task List_legal_entities_sorts_by_legal_name()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var b = await CreateAsync(admin, NewLegalEntity() with { LegalName = "Zsort B " + tag });
        var c = await CreateAsync(admin, NewLegalEntity() with { LegalName = "Zsort C " + tag });
        var a = await CreateAsync(admin, NewLegalEntity() with { LegalName = "Zsort A " + tag });

        (await ListAsync(admin, "pageSize=200&q=" + tag + "&sort=legalName")).Ids.ShouldBe([a.Id, b.Id, c.Id]);
        (await ListAsync(admin, "pageSize=200&q=" + tag + "&sort=-legalName")).Ids.ShouldBe([c.Id, b.Id, a.Id]);
    }

    [Fact(DisplayName = "UI-LE-LIST + ACC-R10: q does not leak a matching legal entity the caller's scope does not cover")]
    public async Task List_legal_entities_q_still_applies_coverage_filtering()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var hidden = await CreateAsync(admin, NewLegalEntity() with { LegalName = "Zcov Hidden " + tag });

        (await ListAsync(admin, "q=" + tag)).Ids.ShouldBe([hidden.Id]);

        var asA = await ListAsync(userA, "q=" + tag);
        asA.Ids.ShouldBeEmpty();
        asA.Total.ShouldBe(0);
    }

    // --- helpers ---------------------------------------------------------------------------

    private static async Task<ListPage> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(new Uri("/v1/legal-entities?" + query, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        var ids = body.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        return new ListPage(body.GetProperty("total").GetInt64(), ids);
    }

    /// <summary>
    /// A fresh user plus one <c>ORG_ADMIN</c> assignment per legal entity in
    /// <paramref name="legalEntityIds"/> (granted by the GLOBAL admin); returns its oid.
    /// </summary>
    private static async Task<string> CreateOrgAdminOfAsync(HttpClient admin, params Guid[] legalEntityIds)
    {
        var tag = Guid.NewGuid().ToString("N");
        var oid = "oid-" + tag;
        var user = await admin.PostAsJsonAsync(
            new Uri("/v1/users", UriKind.Relative),
            new { oid, fullName = "Two-LE Admin " + tag, email = $"twole.{tag}@test.nexus.local", preferredLanguage = "en" },
            Ct);
        user.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await ReadAsync(user)).GetProperty("id").GetGuid();

        foreach (var legalEntityId in legalEntityIds)
        {
            var grant = await admin.PostAsJsonAsync(
                new Uri("/v1/role-assignments", UriKind.Relative),
                new { userId, roleId = OrgAdminRoleId, scopeType = "LEGAL_ENTITY", scopeEntityId = legalEntityId, validFrom = RoundedSystemTimeProvider.Instance.GetUtcNow().AddMinutes(-1) /* backdated: PostgreSQL now() may lag the host clock */ },
                Ct);
            grant.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        return oid;
    }

    private static async Task<List<JsonElement>> ListItemsAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(new Uri("/v1/legal-entities?" + query, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ReadAsync(response)).GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private sealed record ListPage(long Total, IReadOnlyList<Guid> Ids);

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
