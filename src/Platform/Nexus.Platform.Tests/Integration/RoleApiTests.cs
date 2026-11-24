using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Npgsql;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>/v1/roles</c> and <c>/v1/permissions</c> end to end: real host, real JWT, real PostgreSQL
/// (all Flyway migrations + <see cref="OrgFixture"/>, which also seeds <c>db/seed/common</c>'s
/// <c>access.permission</c>/<c>access.role</c> catalog), connecting as <c>svc_platform</c>.
/// </summary>
public sealed class RoleApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri Roles = new("/v1/roles", UriKind.Relative);
    private static readonly Uri Permissions = new("/v1/permissions", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public RoleApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "GET /v1/permissions returns the seeded catalog")]
    public async Task List_permissions_returns_seeded_catalog()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(Permissions, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        var codes = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()).ToList();
        codes.ShouldContain("ROLE:READ");
        codes.ShouldContain("ORG_STRUCTURE:READ");
    }

    [Fact(DisplayName = "POST /v1/roles creates a non-system, active role with no permissions")]
    public async Task Create_role_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.PostAsJsonAsync(Roles, NewRole(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        body.GetProperty("isSystem").GetBoolean().ShouldBeFalse();
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Fact(DisplayName = "POST /v1/roles is idempotent on the same client id with identical content")]
    public async Task Create_role_replay_with_identical_content_returns_200()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewRole();

        var first = await admin.PostAsJsonAsync(Roles, request, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await admin.PostAsJsonAsync(Roles, request, Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "POST /v1/roles with a reused code is a 409 conflict")]
    public async Task Create_role_with_duplicate_code_is_conflict()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var first = NewRole();
        (await admin.PostAsJsonAsync(Roles, first, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = first with { Id = Guid.CreateVersion7() };
        var response = await admin.PostAsJsonAsync(Roles, second, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "PUT /v1/roles/{id} replaces captionI18n/description, not code/isSystem")]
    public async Task Update_role_replaces_caption_and_description()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var response = await PutAsync(
            admin,
            created.Id,
            created.ETag,
            new { captionI18n = new Dictionary<string, string> { ["de"] = "Geändert", ["en"] = "Changed" }, description = "new description" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("description").GetString().ShouldBe("new description");
        body.GetProperty("captionI18n").GetProperty("en").GetString().ShouldBe("Changed");
    }

    [Fact(DisplayName = "POST /v1/roles/{id}/deactivate deactivates a non-system role")]
    public async Task Deactivate_non_system_role_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var response = await DeactivateAsync(admin, created.Id, created.ETag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact(DisplayName = "ACC-R9: a role with isSystem = true cannot be deactivated")]
    public async Task Deactivate_system_role_is_rejected_ACC_R9()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (systemRoleId, etag) = await GetByCodeAsync(admin, "SYSTEM_ADMIN");

        var response = await DeactivateAsync(admin, systemRoleId, etag);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R9");
    }

    [Fact(DisplayName = "ACC-R7: PUT /v1/roles/{id}/permissions closes removed grants and opens new ones, leaving untouched codes alone")]
    public async Task Update_role_permissions_diffs_instead_of_replacing_everything()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var firstPut = await PutPermissionsAsync(admin, created.Id, created.ETag, "ORG_STRUCTURE:READ", "CURRENCY:READ");
        firstPut.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstGrants = await ReadAsync(firstPut);
        var orgStructureValidFrom = firstGrants.EnumerateArray()
            .Single(g => g.GetProperty("code").GetString() == "ORG_STRUCTURE:READ")
            .GetProperty("validFrom").GetDateTimeOffset();

        // Remove CURRENCY:READ, keep ORG_STRUCTURE:READ, add ROLE:READ.
        var secondPut = await PutPermissionsAsync(admin, created.Id, firstPut.Headers.ETag!.Tag, "ORG_STRUCTURE:READ", "ROLE:READ");
        secondPut.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondGrants = await ReadAsync(secondPut);
        var codes = secondGrants.EnumerateArray().Select(g => g.GetProperty("code").GetString()).ToList();

        codes.ShouldBe(["ORG_STRUCTURE:READ", "ROLE:READ"], ignoreOrder: true);

        // ORG_STRUCTURE:READ was untouched: its validFrom did not move.
        var orgStructureAfter = secondGrants.EnumerateArray().Single(g => g.GetProperty("code").GetString() == "ORG_STRUCTURE:READ");
        orgStructureAfter.GetProperty("validFrom").GetDateTimeOffset().ShouldBe(orgStructureValidFrom);

        // CURRENCY:READ was closed, not deleted: it is absent from "now" but present historically.
        var historical = await admin.GetAsync(
            new Uri($"/v1/roles/{created.Id}/permissions?pageSize=50", UriKind.Relative), Ct);
        var currentItems = (await ReadAsync(historical)).GetProperty("items").EnumerateArray()
            .Select(g => g.GetProperty("code").GetString()).ToList();
        currentItems.ShouldNotContain("CURRENCY:READ");
        currentItems.ShouldContain("ORG_STRUCTURE:READ");
        currentItems.ShouldContain("ROLE:READ");
    }

    [Fact(DisplayName = "ACC-R7: resending the exact same permission set changes nothing (same grants, same ETag)")]
    public async Task Update_role_permissions_with_same_set_is_a_no_op()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var first = await PutPermissionsAsync(admin, created.Id, created.ETag, "ORG_STRUCTURE:READ");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstValidFrom = (await ReadAsync(first)).EnumerateArray().Single().GetProperty("validFrom").GetDateTimeOffset();

        var second = await PutPermissionsAsync(admin, created.Id, first.Headers.ETag!.Tag, "ORG_STRUCTURE:READ");
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondValidFrom = (await ReadAsync(second)).EnumerateArray().Single().GetProperty("validFrom").GetDateTimeOffset();

        secondValidFrom.ShouldBe(firstValidFrom); // no close + reopen: the row was left alone.
        second.Headers.ETag!.Tag.ShouldBe(first.Headers.ETag!.Tag); // nor was the role row.
    }

    [Fact(DisplayName = "PUT /v1/roles/{id}/permissions with an unknown code is rejected")]
    public async Task Update_role_permissions_with_unknown_code_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var response = await PutPermissionsAsync(admin, created.Id, created.ETag, "NOT_A_REAL_PERMISSION:READ");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "ACC-R7: an overlapping role_permission window (concurrent add of the same code) is a 409, not a 500")]
    public async Task Update_role_permissions_overlapping_an_unseen_grant_is_a_409_concurrency()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        // Stand-in for the winner of a race: a grant of ROLE:READ the handler does not see as
        // current (it reads valid_to IS NULL) but whose window still covers now(). Opening
        // [now, infinity) for the same code then hits the EXCLUDE constraint, exactly as the
        // loser of two concurrent PUTs adding the same code would.
        await _fixture.Database.ScalarAsync<object>(
            "INSERT INTO access.role_permission (id, role_id, permission_id, valid_from, valid_to, granted_by) " +
            "VALUES ($1, $2, (SELECT id FROM access.permission WHERE code = 'ROLE:READ'), now() - interval '1 hour', now() + interval '1 day', $3)",
            Guid.CreateVersion7(),
            created.Id,
            Org.Admin.Id);

        var response = await PutPermissionsAsync(admin, created.Id, created.ETag, "ROLE:READ");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("CONCURRENCY");
    }

    [Fact(DisplayName = "PUT /v1/roles/{id}/permissions without If-Match is 428 and changes nothing")]
    public async Task Update_role_permissions_without_if_match_is_428()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var response = await PutPermissionsAsync(admin, created.Id, etag: null, "ROLE:READ");

        response.StatusCode.ShouldBe((HttpStatusCode)428);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("PRECONDITION_REQUIRED");
        (await CurrentPermissionCodesAsync(created.Id)).ShouldBeEmpty();
    }

    [Theory(DisplayName = "PUT /v1/roles/{id}/permissions with a stale or unparseable If-Match is 409 CONCURRENCY and changes nothing")]
    [InlineData("stale")]
    [InlineData("garbage")]
    public async Task Update_role_permissions_with_a_stale_if_match_is_409(string kind)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());
        var first = await PutPermissionsAsync(admin, created.Id, created.ETag, "ORG_STRUCTURE:READ");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // created.ETag predates the first PUT, which changed the set and so the role row's xmin.
        var etag = kind == "stale" ? created.ETag : "\"not-a-version\"";
        var response = await PutPermissionsAsync(admin, created.Id, etag, "ROLE:READ");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("CONCURRENCY");
        (await CurrentPermissionCodesAsync(created.Id)).ShouldBe(["ORG_STRUCTURE:READ"]);
    }

    [Fact(DisplayName = "PUT /v1/roles/{id}/permissions with the current If-Match is 200 with a new ETag that the next PUT and GET /v1/roles/{id} agree on")]
    public async Task Update_role_permissions_returns_an_etag_usable_for_the_next_put()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var first = await PutPermissionsAsync(admin, created.Id, created.ETag, "ORG_STRUCTURE:READ");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstETag = first.Headers.ETag!.Tag;
        firstETag.ShouldNotBe(created.ETag);

        var get = await admin.GetAsync(new Uri($"/v1/roles/{created.Id}", UriKind.Relative), Ct);
        get.Headers.ETag!.Tag.ShouldBe(firstETag);

        var second = await PutPermissionsAsync(admin, created.Id, firstETag, "ORG_STRUCTURE:READ", "ROLE:READ");
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.ETag!.Tag.ShouldNotBe(firstETag);
        (await CurrentPermissionCodesAsync(created.Id)).ShouldBe(["ORG_STRUCTURE:READ", "ROLE:READ"], ignoreOrder: true);
    }

    [Theory(DisplayName = "ACC-R11: removing a critical permission from SYSTEM_ADMIN is 422 ACC-R11 and changes nothing")]
    [InlineData("ROLE:READ")]
    [InlineData("ROLE:UPDATE")]
    [InlineData("ROLE_ASSIGNMENT:READ")]
    [InlineData("ROLE_ASSIGNMENT:CREATE")]
    [InlineData("ROLE_ASSIGNMENT:UPDATE")]
    public async Task Removing_a_critical_permission_from_system_admin_is_rejected_ACC_R11(string critical)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (systemAdminId, etag) = await GetByCodeAsync(admin, "SYSTEM_ADMIN");
        var before = await CurrentPermissionCodesAsync(systemAdminId);
        before.ShouldContain(critical);

        var response = await PutPermissionsAsync(admin, systemAdminId, etag, before.Where(c => c != critical).ToArray());

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R11");
        (await CurrentPermissionCodesAsync(systemAdminId)).ShouldBe(before, ignoreOrder: true);
        (await GetByCodeAsync(admin, "SYSTEM_ADMIN")).ETag.ShouldBe(etag);
    }

    [Fact(DisplayName = "ACC-R11: removing a non-critical permission from SYSTEM_ADMIN is 200")]
    public async Task Removing_a_non_critical_permission_from_system_admin_succeeds_ACC_R11()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (systemAdminId, etag) = await GetByCodeAsync(admin, "SYSTEM_ADMIN");
        var before = await CurrentPermissionCodesAsync(systemAdminId);
        const string nonCritical = "CURRENCY:UPDATE";
        before.ShouldContain(nonCritical);

        var response = await PutPermissionsAsync(admin, systemAdminId, etag, before.Where(c => c != nonCritical).ToArray());
        try
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await CurrentPermissionCodesAsync(systemAdminId)).ShouldNotContain(nonCritical);
        }
        finally
        {
            // This class's database only, but other tests here act as the GLOBAL admin: put it back.
            if (response.StatusCode == HttpStatusCode.OK)
            {
                (await PutPermissionsAsync(admin, systemAdminId, response.Headers.ETag!.Tag, before.ToArray()))
                    .StatusCode.ShouldBe(HttpStatusCode.OK);
            }
        }
    }

    [Fact(DisplayName = "ACC-R11: any role other than SYSTEM_ADMIN may drop the five critical permissions (200)")]
    public async Task Other_roles_may_drop_the_critical_permissions_ACC_R11()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());
        string[] critical = ["ROLE:READ", "ROLE:UPDATE", "ROLE_ASSIGNMENT:READ", "ROLE_ASSIGNMENT:CREATE", "ROLE_ASSIGNMENT:UPDATE"];
        var granted = await PutPermissionsAsync(admin, created.Id, created.ETag, critical);
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PutPermissionsAsync(admin, created.Id, granted.Headers.ETag!.Tag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CurrentPermissionCodesAsync(created.Id)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "The role_permission EXCLUDE constraint has the name RoleErrors maps")]
    public async Task Role_permission_exclusion_constraint_name_is_pinned()
    {
        var definition = await _fixture.Database.ScalarAsync<string>(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = 'access.role_permission'::regclass AND conname = $1",
            Nexus.Platform.Application.Roles.RoleErrors.RolePermissionOverlapExclusion);

        definition.ShouldNotBeNull();
        definition.ShouldStartWith("EXCLUDE USING gist");
    }

    // --- helpers ---------------------------------------------------------------------------

    private static CreateRole NewRole()
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return new CreateRole(
            Id: Guid.CreateVersion7(),
            Code: "ROLE-TEST-" + tag,
            CaptionI18n: new Dictionary<string, string> { ["de"] = "Testrolle " + tag, ["en"] = "Test role " + tag },
            Description: null);
    }

    private static async Task<CreatedRole> CreateAsync(HttpClient client, CreateRole request)
    {
        var response = await client.PostAsJsonAsync(Roles, request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        return new CreatedRole(body.GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string etag, object request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/roles/{id}", UriKind.Relative))
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(message, Ct);
    }

    /// <summary><c>PUT /v1/roles/{id}/permissions</c> with <paramref name="codes"/>; no <c>If-Match</c> when <paramref name="etag"/> is <see langword="null"/>.</summary>
    private static async Task<HttpResponseMessage> PutPermissionsAsync(HttpClient client, Guid id, string? etag, params string[] codes)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/roles/{id}/permissions", UriKind.Relative))
        {
            Content = JsonContent.Create(new { permissionCodes = codes }),
        };
        if (etag is not null)
        {
            message.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(message, Ct);
    }

    /// <summary>The role's currently open grants (<c>valid_to IS NULL</c>), read from the database.</summary>
    private async Task<List<string>> CurrentPermissionCodesAsync(Guid roleId)
    {
        await using var command = _fixture.Database.Admin.CreateCommand(
            "SELECT p.code FROM access.role_permission rp JOIN access.permission p ON p.id = rp.permission_id " +
            "WHERE rp.role_id = $1 AND rp.valid_to IS NULL ORDER BY p.code");
        command.Parameters.Add(new NpgsqlParameter { Value = roleId });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var codes = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            codes.Add(reader.GetString(0));
        }

        return codes;
    }

    private static async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid id, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/roles/{id}/deactivate", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    /// <summary>Looks up a seeded <c>db/seed/common</c> role by its <c>code</c> (e.g. <c>SYSTEM_ADMIN</c>), via the API.</summary>
    private async Task<(Guid Id, string ETag)> GetByCodeAsync(HttpClient client, string code)
    {
        await using var connection = new NpgsqlConnection(Org.Database.GetConnectionString("svc_platform"));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT id FROM access.role WHERE code = $1", connection);
        command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = code });
        var id = (Guid)(await command.ExecuteScalarAsync(Ct))!;

        var response = await client.GetAsync(new Uri($"/v1/roles/{id}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (id, response.Headers.ETag!.Tag);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private sealed record CreateRole(Guid? Id, string Code, IReadOnlyDictionary<string, string> CaptionI18n, string? Description);

    private sealed record CreatedRole(Guid Id, string ETag);
}
