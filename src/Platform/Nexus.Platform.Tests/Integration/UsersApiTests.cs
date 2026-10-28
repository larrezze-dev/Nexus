using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>/v1/users</c> end to end: real host, real JWT, real PostgreSQL (all Flyway migrations +
/// <see cref="OrgFixture"/>), connecting as <c>svc_platform</c>. Each test creates its own users
/// (unique oid/email), so tests never depend on each other.
/// </summary>
public sealed class UsersApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri Users = new("/v1/users", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public UsersApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ORG-R4: POST /v1/users creates a PERSON party + app_user, with both events in outbox.message, in one transaction")]
    public async Task Create_user_writes_person_party_app_user_and_events()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewUser();

        var response = await admin.PostAsJsonAsync(Users, request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.ETag.ShouldNotBeNull();
        var body = await ReadAsync(response);
        var userId = body.GetProperty("id").GetGuid();
        var partyId = body.GetProperty("partyId").GetGuid();
        response.Headers.Location!.ToString().ShouldBe($"/v1/users/{userId}");
        userId.Version.ShouldBe(7);
        body.GetProperty("oid").GetString().ShouldBe(request.Oid);
        body.GetProperty("fullName").GetString().ShouldBe(request.FullName);
        body.GetProperty("email").GetString().ShouldBe(request.Email);
        body.GetProperty("preferredLanguage").GetString().ShouldBe("en");
        body.GetProperty("businessUnitId").GetGuid().ShouldBe(Org.BusinessUnitA.Id);
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        // ORG-R4: the user's party is a PERSON (internal: a person of the group).
        (await Scalar<string>("SELECT party_type FROM org.party WHERE id = $1", partyId)).ShouldBe("PERSON");
        (await Scalar<bool>("SELECT is_internal FROM org.party WHERE id = $1", partyId)).ShouldBeTrue();
        (await Scalar<string>("SELECT legal_name FROM org.party WHERE id = $1", partyId)).ShouldBe(request.FullName);
        (await Scalar<Guid>("SELECT party_id FROM org.app_user WHERE id = $1", userId)).ShouldBe(partyId);
        (await Scalar<string>("SELECT external_subject FROM org.app_user WHERE id = $1", userId)).ShouldBe(request.Oid);

        // The JWT's oid was resolved to the admin's app_user and used inside the handler's unit
        // of work: audit columns and the outbox envelope carry that id.
        (await Scalar<Guid>("SELECT created_by FROM org.app_user WHERE id = $1", userId)).ShouldBe(Org.Admin.Id);
        (await Scalar<Guid>("SELECT updated_by FROM org.party WHERE id = $1", partyId)).ShouldBe(Org.Admin.Id);

        var partyEvents = await OutboxAsync(partyId);
        partyEvents.Select(e => e.EventType).ShouldBe(["org.party.created"]);
        var userEvents = await OutboxAsync(userId);
        userEvents.Select(e => e.EventType).ShouldBe(["org.user.created"]);
        userEvents[0].Topic.ShouldBe("nexus.org.user.created");
        userEvents[0].ActorUserId.ShouldBe(Org.Admin.Id);
        userEvents[0].CreatedAt.ShouldBe(partyEvents[0].CreatedAt); // same transaction, one instant

        // EV-R5: no email in user events.
        userEvents[0].Payload.ShouldNotContain("email", Case.Insensitive);
        userEvents[0].Payload.ShouldNotContain(request.Email!, Case.Insensitive);
        userEvents[0].Payload.ShouldContain(request.Oid);
    }

    [Fact(DisplayName = "GET /v1/users/{id} returns the user with the same ETag the create returned")]
    public async Task Get_user_returns_it_with_its_etag()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await CreateAsync(admin, NewUser());

        var response = await admin.GetAsync(new Uri($"/v1/users/{created.Id}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe(created.ETag);
        (await ReadAsync(response)).GetProperty("fullName").GetString().ShouldBe(created.Request.FullName);
    }

    [Fact]
    public async Task Get_unknown_user_returns_404()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(new Uri($"/v1/users/{Guid.CreateVersion7()}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("NOT_FOUND");
    }

    [Fact(DisplayName = "ORG-R9: creating a user with the email of an active user (any case) is 422 ORG-R9 and writes nothing")]
    public async Task Create_with_an_active_users_email_is_rejected_and_rolled_back()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var first = await CreateAsync(admin, NewUser());
        var second = NewUser() with { Email = first.Request.Email!.ToUpperInvariant() };

        var response = await admin.PostAsJsonAsync(Users, second, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R9");

        // The party had been inserted before the unique index fired: everything rolled back,
        // rows and events alike (conventions-testing.md §3, "comando que emite evento" (b)).
        (await Scalar<long>("SELECT count(*) FROM org.party WHERE legal_name = $1", second.FullName)).ShouldBe(0);
        (await Scalar<long>("SELECT count(*) FROM org.app_user WHERE external_subject = $1", second.Oid)).ShouldBe(0);
        (await Scalar<long>("SELECT count(*) FROM outbox.message WHERE payload LIKE $1", "%" + second.Oid + "%")).ShouldBe(0);
    }

    [Fact(DisplayName = "ORG-R9: the email of a deactivated user can be used by a new user")]
    public async Task Email_of_a_deactivated_user_can_be_reused()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var first = await CreateAsync(admin, NewUser());
        (await DeactivateAsync(admin, first.Id, first.ETag)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await admin.PostAsJsonAsync(Users, NewUser() with { Email = first.Request.Email }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "ORG-R9: changing a user's email to another active user's email is 422 ORG-R9")]
    public async Task Update_to_an_active_users_email_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var first = await CreateAsync(admin, NewUser());
        var second = await CreateAsync(admin, NewUser());

        var response = await PutAsync(admin, second.Id, second.ETag, ToUpdate(second.Request) with { Email = first.Request.Email });

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R9");
    }

    [Fact(DisplayName = "ORG-R6: the SYSTEM user is never edited (PUT is 422 ORG-R6, nothing changes)")]
    public async Task Update_system_user_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var etag = await ETagOfAsync(admin, OrgFixture.SystemUserId);

        var response = await PutAsync(admin, OrgFixture.SystemUserId, etag, new UpdateUser(
            Oid: "someone-trying-to-log-in-as-system", FullName: "Not SYSTEM", Email: null, PreferredLanguage: "de", BusinessUnitId: null));

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R6");
        (await Scalar<string>("SELECT external_subject FROM org.app_user WHERE id = $1", OrgFixture.SystemUserId)).ShouldBeNull();
        (await Scalar<string>("SELECT legal_name FROM org.party WHERE id = $1", OrgFixture.SystemUserId)).ShouldBe("SYSTEM");
    }

    [Fact(DisplayName = "ORG-R6: the SYSTEM user is never deactivated (422 ORG-R6)")]
    public async Task Deactivate_system_user_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var etag = await ETagOfAsync(admin, OrgFixture.SystemUserId);

        var response = await DeactivateAsync(admin, OrgFixture.SystemUserId, etag);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R6");
        (await Scalar<bool>("SELECT is_active FROM org.app_user WHERE id = $1", OrgFixture.SystemUserId)).ShouldBeTrue();
    }

    [Fact(DisplayName = "ORG-R6: a user cannot be created with the SYSTEM user's id (422 ORG-R6)")]
    public async Task Create_with_system_id_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.PostAsJsonAsync(Users, NewUser() with { Id = OrgFixture.SystemUserId }, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R6");
    }

    [Fact(DisplayName = "PUT /v1/users/{id} without If-Match is 428")]
    public async Task Update_without_if_match_is_428()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var user = await CreateAsync(admin, NewUser());

        var response = await admin.PutAsJsonAsync(new Uri($"/v1/users/{user.Id}", UriKind.Relative), ToUpdate(user.Request), Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)428);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("PRECONDITION_REQUIRED");
    }

    [Fact(DisplayName = "PUT /v1/users/{id} with an old ETag is 409 CONCURRENCY")]
    public async Task Update_with_a_stale_etag_is_409()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var user = await CreateAsync(admin, NewUser());
        var first = await PutAsync(admin, user.Id, user.ETag, ToUpdate(user.Request) with { PreferredLanguage = "fr" });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await PutAsync(admin, user.Id, user.ETag, ToUpdate(user.Request) with { PreferredLanguage = "it" });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(second)).GetProperty("code").GetString().ShouldBe("CONCURRENCY");
        (await Scalar<string>("SELECT preferred_language FROM org.app_user WHERE id = $1", user.Id)).ShouldBe("fr");
    }

    [Fact(DisplayName = "PUT /v1/users/{id}: a rename made through the party in between also makes the ETag stale (409)")]
    public async Task Update_after_a_party_change_is_409()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var user = await CreateAsync(admin, NewUser());
        await using (var command = _fixture.Database.Admin.CreateCommand("UPDATE org.party SET legal_name = 'Renamed elsewhere' WHERE id = $1"))
        {
            command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = user.PartyId });
            await command.ExecuteNonQueryAsync(Ct);
        }

        var response = await PutAsync(admin, user.Id, user.ETag, ToUpdate(user.Request) with { FullName = "Overwrite" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "PUT /v1/users/{id} with the current ETag: 200, new ETag, user.updated + party.updated events")]
    public async Task Update_with_the_current_etag_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var user = await CreateAsync(admin, NewUser());
        var update = ToUpdate(user.Request) with { FullName = user.Request.FullName + " Renamed", PreferredLanguage = "es", BusinessUnitId = Org.BusinessUnitB.Id };

        var response = await PutAsync(admin, user.Id, user.ETag, update);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldNotBe(user.ETag);
        var body = await ReadAsync(response);
        body.GetProperty("fullName").GetString().ShouldBe(update.FullName);
        body.GetProperty("preferredLanguage").GetString().ShouldBe("es");
        body.GetProperty("businessUnitId").GetGuid().ShouldBe(Org.BusinessUnitB.Id);

        (await OutboxAsync(user.Id)).Select(e => e.EventType).ShouldBe(["org.user.created", "org.user.updated"]);
        (await OutboxAsync(user.PartyId)).Select(e => e.EventType).ShouldBe(["org.party.created", "org.party.updated"]);

        // The new ETag is the one GET returns now.
        (await ETagOfAsync(admin, user.Id)).ShouldBe(response.Headers.ETag.Tag);
    }

    [Fact(DisplayName = "POST /v1/users/{id}/deactivate: 200, isActive=false, user.deactivated; the user is no longer provisioned")]
    public async Task Deactivate_user_succeeds_and_blocks_its_token()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var user = await CreateAsync(admin, NewUser());
        using (var self = _fixture.CreateClientFor(user.Request.Oid))
        {
            (await self.GetAsync(new Uri("/v1/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var response = await DeactivateAsync(admin, user.Id, user.ETag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await OutboxAsync(user.Id)).Select(e => e.EventType).ShouldBe(["org.user.created", "org.user.deactivated"]);

        using var deactivated = _fixture.CreateClientFor(user.Request.Oid);
        var me = await deactivated.GetAsync(new Uri("/v1/me", UriKind.Relative), Ct);
        me.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(me)).GetProperty("code").GetString().ShouldBe("user_not_provisioned");
    }

    [Fact(DisplayName = "POST /v1/users/{id}/deactivate without If-Match is 428")]
    public async Task Deactivate_without_if_match_is_428()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var user = await CreateAsync(admin, NewUser());

        var response = await admin.PostAsync(new Uri($"/v1/users/{user.Id}/deactivate", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)428);
    }

    [Fact(DisplayName = "Idempotent create: same id and content → 200 with the user, no new rows or events")]
    public async Task Create_is_idempotent_for_the_same_content()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewUser() with { Id = Guid.CreateVersion7() };
        var first = await admin.PostAsJsonAsync(Users, request, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var replay = await admin.PostAsJsonAsync(Users, request, Ct);

        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.ETag!.Tag.ShouldBe(first.Headers.ETag!.Tag);
        (await ReadAsync(replay)).GetProperty("id").GetGuid().ShouldBe(request.Id!.Value);
        (await OutboxAsync(request.Id.Value)).Count.ShouldBe(1);
        (await Scalar<long>("SELECT count(*) FROM org.party WHERE legal_name = $1", request.FullName)).ShouldBe(1);
    }

    [Fact(DisplayName = "Idempotent create: same id, different content → 409 CONFLICT")]
    public async Task Create_with_a_reused_id_and_other_content_is_409()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewUser() with { Id = Guid.CreateVersion7() };
        (await admin.PostAsJsonAsync(Users, request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await admin.PostAsJsonAsync(Users, request with { PreferredLanguage = "it" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("CONFLICT");
    }

    [Fact(DisplayName = "An oid already used by another user is 409 CONFLICT")]
    public async Task Create_with_a_used_oid_is_409()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.PostAsJsonAsync(Users, NewUser() with { Oid = Org.UserA.Oid }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("CONFLICT");
    }

    [Fact(DisplayName = "A businessUnitId that does not exist is 422 BUSINESS_UNIT_NOT_FOUND")]
    public async Task Create_with_an_unknown_business_unit_is_422()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.PostAsJsonAsync(Users, NewUser() with { BusinessUnitId = Guid.CreateVersion7() }, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("BUSINESS_UNIT_NOT_FOUND");
    }

    [Theory(DisplayName = "Shape validation errors are 400 VALIDATION")]
    [InlineData("fullName", "   ")]
    [InlineData("preferredLanguage", "pt")]
    [InlineData("email", "not-an-email")]
    public async Task Create_with_an_invalid_shape_is_400(string field, string value)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = field switch
        {
            "fullName" => NewUser() with { FullName = value },
            "preferredLanguage" => NewUser() with { PreferredLanguage = value },
            _ => NewUser() with { Email = value },
        };

        var response = await admin.PostAsJsonAsync(Users, request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("VALIDATION");
    }

    [Fact(DisplayName = "A body without a required property (oid) is 400")]
    public async Task Create_without_oid_is_400()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.PostAsJsonAsync(Users, new { fullName = "No Oid", preferredLanguage = "de" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "GET /v1/users pages and sorts (page, pageSize, sort=-createdAt)")]
    public async Task List_users_pages_and_sorts()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var newest = await CreateAsync(admin, NewUser());

        var response = await admin.GetAsync(new Uri("/v1/users?page=1&pageSize=2&sort=-createdAt", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("page").GetInt32().ShouldBe(1);
        body.GetProperty("pageSize").GetInt32().ShouldBe(2);
        body.GetProperty("total").GetInt64().ShouldBeGreaterThanOrEqualTo(5); // SYSTEM + 3 fixture users + this one
        var items = body.GetProperty("items");
        items.GetArrayLength().ShouldBe(2);
        items[0].GetProperty("id").GetGuid().ShouldBe(newest.Id);
        items[0].GetProperty("fullName").GetString().ShouldBe(newest.Request.FullName);
    }

    [Theory(DisplayName = "GET /v1/users rejects an invalid page, pageSize or sort with 400")]
    [InlineData("page=0")]
    [InlineData("pageSize=201")]
    [InlineData("sort=passwordHash")]
    public async Task List_users_rejects_invalid_parameters(string query)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(new Uri("/v1/users?" + query, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>A valid, unique create request (own oid/email/name), in BU-A.</summary>
    private CreateUser NewUser()
    {
        var tag = Guid.NewGuid().ToString("N");
        return new CreateUser(
            Id: null,
            Oid: "oid-" + tag,
            FullName: "Test User " + tag,
            Email: $"user.{tag}@test.nexus.local",
            PreferredLanguage: "en",
            BusinessUnitId: Org.BusinessUnitA.Id);
    }

    private static UpdateUser ToUpdate(CreateUser request) =>
        new(request.Oid, request.FullName, request.Email, request.PreferredLanguage, request.BusinessUnitId);

    private static async Task<CreatedUser> CreateAsync(HttpClient client, CreateUser request)
    {
        var response = await client.PostAsJsonAsync(Users, request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        return new CreatedUser(body.GetProperty("id").GetGuid(), body.GetProperty("partyId").GetGuid(), response.Headers.ETag!.Tag, request);
    }

    private static async Task<string> ETagOfAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync(new Uri($"/v1/users/{id}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response.Headers.ETag!.Tag;
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string etag, UpdateUser body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/users/{id}", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid id, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/users/{id}/deactivate", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private Task<T?> Scalar<T>(string sql, params object[] parameters) => _fixture.Database.ScalarAsync<T>(sql, parameters);

    private async Task<List<OutboxRow>> OutboxAsync(Guid aggregateId)
    {
        await using var command = _fixture.Database.Admin.CreateCommand(
            "SELECT event_type, topic, payload, (headers->>'actor_user_id')::uuid, created_at FROM outbox.message " +
            "WHERE aggregate_id = $1 ORDER BY created_at, id");
        command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = aggregateId });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<OutboxRow>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new OutboxRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3), reader.GetDateTime(4)));
        }

        return rows;
    }

    private sealed record CreateUser(Guid? Id, string Oid, string FullName, string? Email, string PreferredLanguage, Guid? BusinessUnitId);

    private sealed record UpdateUser(string? Oid, string FullName, string? Email, string PreferredLanguage, Guid? BusinessUnitId);

    private sealed record CreatedUser(Guid Id, Guid PartyId, string ETag, CreateUser Request);

    private sealed record OutboxRow(string EventType, string Topic, string Payload, Guid ActorUserId, DateTime CreatedAt);
}
