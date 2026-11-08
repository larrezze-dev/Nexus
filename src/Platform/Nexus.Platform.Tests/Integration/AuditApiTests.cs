using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>GET /v1/audit/{aggregateType}/{aggregateId}</c> (<c>listAuditEvents</c>,
/// <c>contracts/openapi/_shared/audit.yaml</c>, conventions-api.md §7) end to end: real host, real
/// JWTs, real PostgreSQL (permissions, org names) and a real ClickHouse holding <c>event_log</c>.
/// Seeded roles: SYSTEM_ADMIN (GLOBAL) and ORG_ADMIN on LE-A (UserA) both hold AUDIT:READ.
/// </summary>
public sealed class AuditApiTests : IClassFixture<AuditApiFixture>
{
    private const string FieldCaptureRoleId = "00000000-0000-7000-8000-0000000d000a";

    private readonly AuditApiFixture _fixture;

    public AuditApiTests(AuditApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Trail(string aggregateType, Guid id, string? query = null) =>
        new($"/v1/audit/{aggregateType}/{id}" + (query is null ? string.Empty : "?" + query), UriKind.Relative);

    [Fact(DisplayName = "conventions-api §7: the GLOBAL admin gets the full trail, aggregateVersion descending, with previousPayload and the resolved actor name")]
    public async Task Admin_gets_the_full_trail_newest_first()
    {
        var aggregateId = Guid.CreateVersion7();
        await _fixture.InsertEventsAsync(
            EventLogRow.Create(Org.LegalEntityB.Id, "warehouse", aggregateId, 1, """{"code":"WH-1","isActive":true}""", Org.Admin.Id, "created"),
            EventLogRow.Create(Org.LegalEntityB.Id, "warehouse", aggregateId, 2, """{"code":"WH-1","isActive":false}""", Guid.Empty));

        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var response = await admin.GetAsync(Trail("warehouse", aggregateId), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("page").GetInt32().ShouldBe(1);
        body.GetProperty("pageSize").GetInt32().ShouldBe(50);
        body.GetProperty("total").GetInt64().ShouldBe(2);

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("aggregateVersion").GetInt64()).ShouldBe([2L, 1L]);

        var newest = items[0];
        newest.GetProperty("eventType").GetString().ShouldBe("org.warehouse.updated");
        newest.GetProperty("payload").GetProperty("isActive").GetBoolean().ShouldBeFalse();
        newest.GetProperty("previousPayload").GetProperty("isActive").GetBoolean().ShouldBeTrue();
        newest.GetProperty("actorUserId").GetGuid().ShouldBe(Guid.Empty);
        newest.GetProperty("actorDisplayName").ValueKind.ShouldBe(JsonValueKind.Null);

        var oldest = items[1];
        oldest.GetProperty("previousPayload").ValueKind.ShouldBe(JsonValueKind.Null);
        oldest.GetProperty("actorUserId").GetGuid().ShouldBe(Org.Admin.Id);
        oldest.GetProperty("actorDisplayName").GetString().ShouldBe(Org.Admin.FullName);
    }

    [Fact(DisplayName = "audit.yaml AuditEvent: every field is present with the contract's type; occurredAt is ISO-8601 UTC with microseconds; payloads are JSON objects, not strings")]
    public async Task Response_matches_the_AuditEvent_shape()
    {
        var aggregateId = Guid.CreateVersion7();
        await _fixture.InsertEventsAsync(
            EventLogRow.Create(Org.LegalEntityB.Id, "warehouse", aggregateId, 1, """{"code":"WH-S"}""", Org.Admin.Id, "created"));

        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var body = await ReadAsync(await admin.GetAsync(Trail("warehouse", aggregateId), Ct));

        var item = body.GetProperty("items").EnumerateArray().Single();
        string[] required =
        [
            "eventId", "occurredAt", "eventType", "aggregateVersion", "actorUserId", "actorDisplayName", "correlationId", "payload", "previousPayload",
        ];
        item.EnumerateObject().Select(p => p.Name).Order().ShouldBe(required.Order());

        item.GetProperty("eventId").GetGuid().ShouldNotBe(Guid.Empty);
        item.GetProperty("correlationId").GetGuid().ShouldNotBe(Guid.Empty);
        item.GetProperty("aggregateVersion").ValueKind.ShouldBe(JsonValueKind.Number);
        item.GetProperty("occurredAt").GetString().ShouldBe("2026-11-08T10:01:00.000123Z");
        item.GetProperty("payload").ValueKind.ShouldBe(JsonValueKind.Object);
        item.GetProperty("payload").GetProperty("code").GetString().ShouldBe("WH-S");
    }

    [Fact(DisplayName = "audit.yaml scope: an ORG_ADMIN of LE-A sees LE-A rows and gets 200 with an empty page (not 404) for an LE-B aggregate")]
    public async Task Scope_is_enforced_by_the_query_and_an_invisible_aggregate_is_an_empty_page()
    {
        var ofA = Guid.CreateVersion7();
        var ofB = Guid.CreateVersion7();
        await _fixture.InsertEventsAsync(
            EventLogRow.Create(Org.LegalEntityA.Id, "warehouse", ofA, 1, """{"code":"A"}""", Org.Admin.Id, "created"),
            EventLogRow.Create(Org.LegalEntityB.Id, "warehouse", ofB, 1, """{"code":"B"}""", Org.Admin.Id, "created"));

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);

        var own = await ReadAsync(await userA.GetAsync(Trail("warehouse", ofA), Ct));
        own.GetProperty("total").GetInt64().ShouldBe(1);

        var foreign = await userA.GetAsync(Trail("warehouse", ofB), Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.OK);
        var empty = await ReadAsync(foreign);
        empty.GetProperty("items").GetArrayLength().ShouldBe(0);
        empty.GetProperty("total").GetInt64().ShouldBe(0);
    }

    [Fact(DisplayName = "EV-R5: a PII row comes back with payload and previousPayload null (headers only)")]
    public async Task Pii_rows_have_null_payloads()
    {
        var aggregateId = Guid.CreateVersion7();
        await _fixture.InsertEventsAsync(
            EventLogRow.Create(Org.LegalEntityB.Id, "party", aggregateId, 1, string.Empty, Org.Admin.Id, "created"),
            EventLogRow.Create(Org.LegalEntityB.Id, "party", aggregateId, 2, string.Empty, Org.Admin.Id));

        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var body = await ReadAsync(await admin.GetAsync(Trail("party", aggregateId), Ct));

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items.ShouldAllBe(i => i.GetProperty("payload").ValueKind == JsonValueKind.Null
            && i.GetProperty("previousPayload").ValueKind == JsonValueKind.Null);
    }

    [Fact(DisplayName = "conventions-api §4: page and pageSize slice the trail and total counts every visible event")]
    public async Task Paging_slices_the_trail()
    {
        var aggregateId = Guid.CreateVersion7();
        await _fixture.InsertEventsAsync(
            Enumerable.Range(1, 5)
                .Select(v => EventLogRow.Create(Org.LegalEntityB.Id, "warehouse", aggregateId, (ulong)v, $$"""{"v":{{v}}}""", Org.Admin.Id))
                .ToArray());

        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var body = await ReadAsync(await admin.GetAsync(Trail("warehouse", aggregateId, "page=2&pageSize=2"), Ct));

        body.GetProperty("page").GetInt32().ShouldBe(2);
        body.GetProperty("pageSize").GetInt32().ShouldBe(2);
        body.GetProperty("total").GetInt64().ShouldBe(5);
        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("aggregateVersion").GetInt64()).ShouldBe([3L, 2L]);
    }

    [Fact(DisplayName = "conventions-api §7 / ACC permission AUDIT:READ: a user whose roles lack AUDIT:READ gets 403")]
    public async Task User_without_audit_read_is_forbidden()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var tag = Guid.NewGuid().ToString("N");
        var oid = "oid-" + tag;
        var user = await admin.PostAsJsonAsync(
            new Uri("/v1/users", UriKind.Relative),
            new { oid, fullName = "Field " + tag, email = $"field.{tag}@test.nexus.local", preferredLanguage = "en" },
            Ct);
        user.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await ReadAsync(user)).GetProperty("id").GetGuid();

        // FIELD_CAPTURE has no standing permissions (db/seed/common): no AUDIT:READ.
        var grant = await admin.PostAsJsonAsync(
            new Uri("/v1/role-assignments", UriKind.Relative),
            new
            {
                userId,
                roleId = FieldCaptureRoleId,
                scopeType = "LEGAL_ENTITY",
                scopeEntityId = Org.LegalEntityA.Id,
                validFrom = RoundedSystemTimeProvider.Instance.GetUtcNow().AddMinutes(-1),
            },
            Ct);
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var field = _fixture.CreateClientFor(oid);
        var response = await field.GetAsync(Trail("warehouse", Guid.CreateVersion7()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");
    }

    [Fact(DisplayName = "conventions-api §6: no token is 401")]
    public async Task Anonymous_is_unauthorized()
    {
        using var anonymous = _fixture.CreateClient();

        (await anonymous.GetAsync(Trail("warehouse", Guid.CreateVersion7()), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory(DisplayName = "audit.yaml aggregateType pattern / conventions-api §4: a bad aggregateType, page or pageSize is a 400 VALIDATION problem naming the field")]
    [InlineData("Warehouse", null, "aggregateType")]
    [InlineData("ware--house", null, "aggregateType")]
    [InlineData("warehouse", "page=0", "page")]
    [InlineData("warehouse", "pageSize=0", "pageSize")]
    [InlineData("warehouse", "pageSize=201", "pageSize")]
    public async Task Invalid_input_is_a_validation_problem(string aggregateType, string? query, string field)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(Trail(aggregateType, Guid.CreateVersion7(), query), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("VALIDATION");
        problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ShouldContain(field);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;
}
