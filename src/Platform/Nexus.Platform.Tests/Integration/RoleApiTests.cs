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

        var firstPut = await admin.PutAsJsonAsync(
            new Uri($"/v1/roles/{created.Id}/permissions", UriKind.Relative),
            new { permissionCodes = new List<string> { "ORG_STRUCTURE:READ", "CURRENCY:READ" } },
            Ct);
        firstPut.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstGrants = await ReadAsync(firstPut);
        var orgStructureValidFrom = firstGrants.EnumerateArray()
            .Single(g => g.GetProperty("code").GetString() == "ORG_STRUCTURE:READ")
            .GetProperty("validFrom").GetDateTimeOffset();

        // Remove CURRENCY:READ, keep ORG_STRUCTURE:READ, add ROLE:READ.
        var secondPut = await admin.PutAsJsonAsync(
            new Uri($"/v1/roles/{created.Id}/permissions", UriKind.Relative),
            new { permissionCodes = new List<string> { "ORG_STRUCTURE:READ", "ROLE:READ" } },
            Ct);
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

    [Fact(DisplayName = "ACC-R7: resending the exact same permission set changes nothing")]
    public async Task Update_role_permissions_with_same_set_is_a_no_op()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());
        var uri = new Uri($"/v1/roles/{created.Id}/permissions", UriKind.Relative);

        var first = await admin.PutAsJsonAsync(uri, new { permissionCodes = new List<string> { "ORG_STRUCTURE:READ" } }, Ct);
        var firstValidFrom = (await ReadAsync(first)).EnumerateArray().Single().GetProperty("validFrom").GetDateTimeOffset();

        var second = await admin.PutAsJsonAsync(uri, new { permissionCodes = new List<string> { "ORG_STRUCTURE:READ" } }, Ct);
        var secondValidFrom = (await ReadAsync(second)).EnumerateArray().Single().GetProperty("validFrom").GetDateTimeOffset();

        secondValidFrom.ShouldBe(firstValidFrom); // no close + reopen: the row was left alone.
    }

    [Fact(DisplayName = "PUT /v1/roles/{id}/permissions with an unknown code is rejected")]
    public async Task Update_role_permissions_with_unknown_code_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewRole());

        var response = await admin.PutAsJsonAsync(
            new Uri($"/v1/roles/{created.Id}/permissions", UriKind.Relative),
            new { permissionCodes = new List<string> { "NOT_A_REAL_PERMISSION:READ" } },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
