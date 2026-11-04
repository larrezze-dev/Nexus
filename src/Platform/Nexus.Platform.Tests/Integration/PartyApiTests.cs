using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// Scope of the <c>/v1/parties</c> writes end to end: real host, real JWT, real PostgreSQL (all
/// Flyway migrations + <see cref="OrgFixture"/>), connecting as <c>svc_platform</c>.
/// <para>
/// A legal entity, business unit or group has its own internal party (ORG-R1) holding its
/// legal name, tax id and ERP code; <c>PARTY:UPDATE</c> (<c>x-scope: none</c>) must not be a
/// back door around the scope checks of the entity's own endpoints (ACC-R10). Any other party
/// stays unscoped.
/// </para>
/// </summary>
public sealed class PartyApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri Parties = new("/v1/parties", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public PartyApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ACC-R10 on party writes: an ORG_ADMIN of LE-A cannot update or deactivate the party backing LE-B (403, nothing changes)")]
    public async Task Party_backing_another_legal_entity_is_forbidden()
    {
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var before = await GetAsync(userA, Org.LegalEntityB.PartyId);

        var update = await PutAsync(userA, before.Id, before.ETag, before.ToUpdate() with { LegalName = "Hijacked LE-B", TaxId = "CHE-000.000.000" });

        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(update)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");
        (await DeactivateAsync(userA, before.Id, before.ETag)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var after = await GetAsync(userA, Org.LegalEntityB.PartyId);
        after.ETag.ShouldBe(before.ETag);
        after.Body.GetProperty("legalName").GetString().ShouldBe(before.Body.GetProperty("legalName").GetString());
        after.Body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Fact(DisplayName = "ACC-R10 on party writes: an ORG_ADMIN of LE-A cannot update or deactivate the party backing a business unit of LE-B (403)")]
    public async Task Party_backing_a_business_unit_of_another_legal_entity_is_forbidden()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var partyId = await CreateBusinessUnitAsync(admin, Org.LegalEntityB.Id);
        var before = await GetAsync(userA, partyId);

        var update = await PutAsync(userA, partyId, before.ETag, before.ToUpdate() with { LegalName = "Hijacked BU" });

        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await DeactivateAsync(userA, partyId, before.ETag)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(userA, partyId)).ETag.ShouldBe(before.ETag);

        // Same for the fixture's own BU-B (read-only check: the call is refused).
        var buB = await GetAsync(userA, Org.BusinessUnitB.PartyId);
        (await PutAsync(userA, buB.Id, buB.ETag, buB.ToUpdate() with { LegalName = "Hijacked BU-B" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "ACC-R10 on party writes: an ORG_ADMIN of LE-A can update the party backing LE-A (200)")]
    public async Task Party_backing_the_callers_own_legal_entity_is_allowed()
    {
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var before = await GetAsync(userA, Org.LegalEntityA.PartyId);

        // Same content (fixture rows must keep their values): the request still runs the scope
        // check, which is what this test is about, and is accepted.
        var response = await PutAsync(userA, before.Id, before.ETag, before.ToUpdate());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("legalName").GetString().ShouldBe(before.Body.GetProperty("legalName").GetString());
    }

    [Fact(DisplayName = "ACC-R10 on party writes: an ORG_ADMIN of LE-A can update and deactivate the party backing a business unit of LE-A (200)")]
    public async Task Party_backing_a_business_unit_of_the_callers_legal_entity_is_allowed()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var partyId = await CreateBusinessUnitAsync(admin, Org.LegalEntityA.Id);
        var before = await GetAsync(userA, partyId);

        var update = await PutAsync(userA, partyId, before.ETag, before.ToUpdate() with { LegalName = "Renamed BU-A child", TaxId = "CHE-111.222.333" });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(update)).GetProperty("legalName").GetString().ShouldBe("Renamed BU-A child");

        var deactivate = await DeactivateAsync(userA, partyId, update.Headers.ETag!.Tag);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "ACC-R10 on party writes: the party backing the group needs GROUP coverage (LE-A ORG_ADMIN 403, GLOBAL admin 200)")]
    public async Task Party_backing_the_group_needs_group_coverage()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var groupPartyId = await _fixture.Database.ScalarAsync<Guid>("SELECT party_id FROM org.\"group\" WHERE id = $1", Org.GroupId);
        var before = await GetAsync(admin, groupPartyId);

        (await PutAsync(userA, groupPartyId, before.ETag, before.ToUpdate() with { LegalName = "Hijacked group" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(admin, groupPartyId)).ETag.ShouldBe(before.ETag);

        // Same content: the fixture's group keeps its values.
        (await PutAsync(admin, groupPartyId, before.ETag, before.ToUpdate())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "ACC-R10 on party writes (ADR-0015): a GROUP-scoped grant covers the group's own party and its legal entities' parties (200)")]
    public async Task Group_scoped_grant_covers_the_group_party()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var tag = Guid.NewGuid().ToString("N");
        var oid = "oid-" + tag;
        var user = await admin.PostAsJsonAsync(
            new Uri("/v1/users", UriKind.Relative),
            new { oid, fullName = "Group Admin " + tag, email = $"groupadmin.{tag}@test.nexus.local", preferredLanguage = "en" },
            Ct);
        user.StatusCode.ShouldBe(HttpStatusCode.Created);
        var grant = await admin.PostAsJsonAsync(
            new Uri("/v1/role-assignments", UriKind.Relative),
            new
            {
                userId = (await ReadAsync(user)).GetProperty("id").GetGuid(),
                roleId = new Guid("00000000-0000-7000-8000-0000000d0002"), // ORG_ADMIN
                scopeType = "GROUP",
                scopeEntityId = Org.GroupId,
                validFrom = DateTimeOffset.UtcNow.AddMinutes(-1), // PostgreSQL now() may lag the host clock
            },
            Ct);
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var groupAdmin = _fixture.CreateClientFor(oid);
        var groupPartyId = await _fixture.Database.ScalarAsync<Guid>("SELECT party_id FROM org.\"group\" WHERE id = $1", Org.GroupId);
        var groupParty = await GetAsync(groupAdmin, groupPartyId);
        var leB = await GetAsync(groupAdmin, Org.LegalEntityB.PartyId);

        // Same content: the fixture's rows keep their values.
        (await PutAsync(groupAdmin, groupPartyId, groupParty.ETag, groupParty.ToUpdate())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutAsync(groupAdmin, leB.Id, leB.ETag, leB.ToUpdate())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Party writes: a party backing no org entity stays unscoped (the LE-A and LE-B ORG_ADMINs can both update it: 200)")]
    public async Task External_party_stays_unscoped()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        using var userB = _fixture.CreateClientFor(Org.UserB.Oid);
        var created = await admin.PostAsJsonAsync(Parties, new
        {
            partyType = "ORGANIZATION",
            legalName = "External Supplier " + Guid.NewGuid().ToString("N"),
            isInternal = false,
        }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var partyId = (await ReadAsync(created)).GetProperty("id").GetGuid();
        var before = await GetAsync(userA, partyId);

        var byA = await PutAsync(userA, partyId, before.ETag, before.ToUpdate() with { DisplayName = "Edited by A" });
        byA.StatusCode.ShouldBe(HttpStatusCode.OK);

        var byB = await PutAsync(userB, partyId, byA.Headers.ETag!.Tag, before.ToUpdate() with { DisplayName = "Edited by B" });
        byB.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(byB)).GetProperty("displayName").GetString().ShouldBe("Edited by B");
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>Creates a business unit in <paramref name="legalEntityId"/> (as GLOBAL) and returns its party id.</summary>
    private static async Task<Guid> CreateBusinessUnitAsync(HttpClient admin, Guid legalEntityId)
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var response = await admin.PostAsJsonAsync(new Uri("/v1/business-units", UriKind.Relative), new
        {
            id = Guid.CreateVersion7(),
            legalName = "Party Test BU " + tag,
            legalEntityId,
            code = "BU-PTY-" + tag,
            captionI18n = new Dictionary<string, string> { ["de"] = "Einheit " + tag, ["en"] = "Unit " + tag },
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadAsync(response)).GetProperty("partyId").GetGuid();
    }

    private static Uri PartyUri(Guid id) => new($"/v1/parties/{id}", UriKind.Relative);

    private static async Task<LoadedParty> GetAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync(PartyUri(id), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return new LoadedParty(id, response.Headers.ETag!.Tag, await ReadAsync(response));
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string etag, UpdateParty body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, PartyUri(id)) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid id, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/parties/{id}/deactivate", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private sealed record UpdateParty(string LegalName, string? DisplayName, string? TaxId, string? ErpCompanyCode);

    private sealed record LoadedParty(Guid Id, string ETag, JsonElement Body)
    {
        public UpdateParty ToUpdate() => new(
            Body.GetProperty("legalName").GetString()!,
            OptionalString("displayName"),
            OptionalString("taxId"),
            OptionalString("erpCompanyCode"));

        private string? OptionalString(string name) =>
            Body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
