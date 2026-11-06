using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Application;
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

    [Fact(DisplayName = "ACC-R4/ACC-R10 (intentional): a WAREHOUSE-scoped grant does not cover its own warehouse's parent business unit (403)")]
    public async Task Warehouse_scoped_grant_does_not_cover_its_parent_business_unit()
    {
        // INTENTIONAL, do not "fix": coverage only flows downwards (GLOBAL ⊃ GROUP ⊃ LEGAL_ENTITY
        // ⊃ BUSINESS_UNIT ⊃ WAREHOUSE). WhA holds ORG_STRUCTURE:READ on WH-A only, so it can read
        // WH-A but not BU-A, even though BU-A is WH-A's parent. If this ever turns into a 200,
        // a narrow grant has started to leak upwards: that needs an explicit decision, not a
        // silent change.
        using var whA = _fixture.CreateClientFor(Org.WhA.Oid);
        (await whA.GetAsync(new Uri($"/v1/warehouses/{Org.WarehouseA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _fixture.Database.ScalarAsync<Guid>("SELECT business_unit_id FROM org.warehouse WHERE id = $1", Org.WarehouseA))
            .ShouldBe(Org.BusinessUnitA.Id);

        var response = await whA.GetAsync(BusinessUnitUri(Org.BusinessUnitA.Id), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");
    }

    [Fact(DisplayName = "ACC-R10 on listBusinessUnits: rows are filtered like getBusinessUnit (LE-A ORG_ADMIN: only LE-A; GLOBAL: all)")]
    public async Task List_business_units_is_filtered_by_the_callers_coverage()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        await CreateAsync(admin, NewBusinessUnit(Org.LegalEntityB.Id));

        var all = await ListAsync(admin, "pageSize=200");
        all.Ids.ShouldContain(Org.BusinessUnitA.Id);
        all.Ids.ShouldContain(Org.BusinessUnitB.Id);
        all.Total.ShouldBe(await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM org.business_unit"));

        var ofA = await ListAsync(userA, "pageSize=200");
        ofA.Ids.ShouldContain(Org.BusinessUnitA.Id);
        ofA.Ids.ShouldNotContain(Org.BusinessUnitB.Id);
        ofA.Items.ShouldAllBe(b => b.GetProperty("legalEntityId").GetGuid() == Org.LegalEntityA.Id);
        ofA.Total.ShouldBe(await _fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM org.business_unit WHERE legal_entity_id = $1", Org.LegalEntityA.Id));
    }

    [Fact(DisplayName = "ACC-R10 on listBusinessUnits: pagination and total apply to the coverage-filtered rows, not the unfiltered ones")]
    public async Task List_business_units_paginates_and_counts_after_coverage_filtering()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        for (var i = 0; i < 2; i++)
        {
            await CreateAsync(admin, NewBusinessUnit(Org.LegalEntityA.Id));
            await CreateAsync(admin, NewBusinessUnit(Org.LegalEntityB.Id));
        }

        // What UserA should see, in the listing's default order (code, then id).
        var expected = await IdsAsync("SELECT id FROM org.business_unit WHERE legal_entity_id = $1 ORDER BY code, id", Org.LegalEntityA.Id);
        expected.Count.ShouldBeGreaterThanOrEqualTo(3);
        var unfilteredTotal = await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM org.business_unit");
        unfilteredTotal.ShouldBeGreaterThan(expected.Count);

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var seen = new List<Guid>();
        var pages = (expected.Count + 1) / 2;
        for (var page = 1; page <= pages; page++)
        {
            var current = await ListAsync(userA, $"pageSize=2&page={page}");
            current.Total.ShouldBe(expected.Count);
            current.Ids.Count.ShouldBe(Math.Min(2, expected.Count - seen.Count));
            seen.AddRange(current.Ids);
        }

        seen.ShouldBe(expected);
        var beyond = await ListAsync(userA, $"pageSize=2&page={pages + 1}");
        beyond.Ids.ShouldBeEmpty();
        beyond.Total.ShouldBe(expected.Count);
    }

    [Fact(DisplayName = "ACC-R10 on listBusinessUnits: a BUSINESS_UNIT-scoped grant lists exactly its own business unit (LE not covered)")]
    public async Task List_business_units_with_a_business_unit_scoped_grant()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        await CreateAsync(admin, NewBusinessUnit(Org.LegalEntityA.Id)); // a sibling it must not see

        var tag = Guid.NewGuid().ToString("N");
        var oid = "oid-" + tag;
        var user = await admin.PostAsJsonAsync(
            new Uri("/v1/users", UriKind.Relative),
            new { oid, fullName = "BU Admin " + tag, email = $"buadmin.{tag}@test.nexus.local", preferredLanguage = "en" },
            Ct);
        user.StatusCode.ShouldBe(HttpStatusCode.Created);
        var grant = await admin.PostAsJsonAsync(
            new Uri("/v1/role-assignments", UriKind.Relative),
            new
            {
                userId = (await ReadAsync(user)).GetProperty("id").GetGuid(),
                roleId = new Guid("00000000-0000-7000-8000-0000000d0002"), // ORG_ADMIN
                scopeType = "BUSINESS_UNIT",
                scopeEntityId = Org.BusinessUnitA.Id,
                validFrom = RoundedSystemTimeProvider.Instance.GetUtcNow().AddMinutes(-1) /* backdated: PostgreSQL now() may lag the host clock */,
            },
            Ct);
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var buAdmin = _fixture.CreateClientFor(oid);
        var page = await ListAsync(buAdmin, "pageSize=200");

        page.Ids.ShouldBe([Org.BusinessUnitA.Id]);
        page.Total.ShouldBe(1);
        (await buAdmin.GetAsync(BusinessUnitUri(Org.BusinessUnitA.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- helpers ---------------------------------------------------------------------------

    private static async Task<ListPage> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(new Uri("/v1/business-units?" + query, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        var items = body.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        return new ListPage(body.GetProperty("total").GetInt64(), items, items.Select(e => e.GetProperty("id").GetGuid()).ToList());
    }

    private async Task<List<Guid>> IdsAsync(string sql, Guid parameter)
    {
        await using var command = _fixture.Database.Admin.CreateCommand(sql);
        command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = parameter });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(Ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private sealed record ListPage(long Total, IReadOnlyList<JsonElement> Items, IReadOnlyList<Guid> Ids);

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
