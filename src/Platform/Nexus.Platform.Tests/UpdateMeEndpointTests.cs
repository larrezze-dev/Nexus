using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Nexus.Platform.Tests.Integration;
using Npgsql;
using Shouldly;

namespace Nexus.Platform.Tests;

/// <summary>
/// <c>PATCH /v1/me</c> (<c>updateMe</c>) against the real host and a real database: the caller
/// changes its own <c>preferredLanguage</c>, without <c>If-Match</c> (conventions-api.md §5
/// exception). Each test creates its own user (unique oid) through <c>POST /v1/users</c>, so
/// tests never depend on each other nor on the shared <see cref="OrgFixture"/> users.
/// </summary>
public sealed class UpdateMeEndpointTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri Me = new("/v1/me", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public UpdateMeEndpointTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "UI-ME: PATCH /v1/me without If-Match changes the language: 200 with the same Me as GET /v1/me, persisted")]
    public async Task Patch_changes_the_language_and_returns_me()
    {
        var user = await CreateUserAsync();
        using var client = _fixture.CreateClientFor(user.Oid);

        var response = await PatchAsync(client, new { preferredLanguage = "es" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldBeNull();
        var patched = await ReadAsync(response);
        patched.GetProperty("preferredLanguage").GetString().ShouldBe("es");
        patched.GetProperty("userId").GetGuid().ShouldBe(user.Id);
        patched.GetProperty("partyId").GetGuid().ShouldBe(user.PartyId);
        patched.GetProperty("businessUnitId").GetGuid().ShouldBe(Org.BusinessUnitA.Id);
        patched.GetProperty("oid").GetString().ShouldBe(user.Oid);
        patched.GetProperty("provisioned").GetBoolean().ShouldBeTrue();

        (await Scalar<string>("SELECT preferred_language FROM org.app_user WHERE id = $1", user.Id)).ShouldBe("es");
        (await Scalar<Guid>("SELECT updated_by FROM org.app_user WHERE id = $1", user.Id)).ShouldBe(user.Id);

        // Same body as GET /v1/me (one builder for both), now with the new language.
        var get = await client.GetAsync(Me, Ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await ReadAsync(get);
        me.GetRawText().ShouldBe(patched.GetRawText());
    }

    [Fact(DisplayName = "EV-R4: a language change writes one org.user.updated with the user's full state, actor = the user")]
    public async Task Patch_writes_one_full_state_user_updated()
    {
        var user = await CreateUserAsync();
        using var client = _fixture.CreateClientFor(user.Oid);

        (await PatchAsync(client, new { preferredLanguage = "fr" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var events = await OutboxAsync(user.Id);
        events.Select(e => (e.Version, e.EventType)).ShouldBe([(1L, "org.user.created"), (2L, "org.user.updated")]);
        var updated = events[1];
        updated.Topic.ShouldBe("nexus.org.user.updated");
        updated.ActorUserId.ShouldBe(user.Id);
        (await Scalar<long>("SELECT aggregate_version FROM org.app_user WHERE id = $1", user.Id)).ShouldBe(2);

        using var payload = JsonDocument.Parse(updated.Payload);
        var root = payload.RootElement;
        root.GetProperty("id").GetString().ShouldBe(user.Id.ToString());
        root.GetProperty("partyId").GetString().ShouldBe(user.PartyId.ToString());
        root.GetProperty("businessUnitId").GetProperty("string").GetString().ShouldBe(Org.BusinessUnitA.Id.ToString());
        root.GetProperty("externalSubject").GetProperty("string").GetString().ShouldBe(user.Oid);
        root.GetProperty("preferredLanguage").GetString().ShouldBe("fr");
        root.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        // EV-R5: still no email in user events.
        updated.Payload.ShouldNotContain("email", Case.Insensitive);
    }

    [Fact(DisplayName = "UI-ME: PATCH with the language the user already has is 200, writes nothing and emits no event")]
    public async Task Patch_with_the_same_language_is_a_no_op()
    {
        var user = await CreateUserAsync();
        using var client = _fixture.CreateClientFor(user.Oid);
        var xminBefore = await XminAsync(user.Id);

        var response = await PatchAsync(client, new { preferredLanguage = "en" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("preferredLanguage").GetString().ShouldBe("en");
        (await XminAsync(user.Id)).ShouldBe(xminBefore);
        (await OutboxAsync(user.Id)).Select(e => e.EventType).ShouldBe(["org.user.created"]);
    }

    /// <summary>
    /// Validator failures (unsupported or blank language) are the dispatcher's 400
    /// <c>VALIDATION</c>, like <c>PUT /v1/users/{id}</c>. Body-shape failures the JSON binder
    /// rejects before any handler (unknown property, <c>additionalProperties: false</c>; missing
    /// required property; not JSON) are a 400 problem too: <c>VALIDATION</c> when ASP.NET Core
    /// throws them (Development, the test host) or <c>BAD_REQUEST</c> via the status-code pages
    /// otherwise; both are the contract's 400.
    /// </summary>
    [Theory(DisplayName = "UI-ME: an invalid body is a 400 and changes nothing (unsupported language, unknown property, missing field)")]
    [InlineData("""{"preferredLanguage":"pt"}""", true)]
    [InlineData("""{"preferredLanguage":"ES"}""", true)]
    [InlineData("""{"preferredLanguage":""}""", true)]
    [InlineData("""{"preferredLanguage":null}""", true)]
    [InlineData("""{}""", false)]
    [InlineData("""{"preferredLanguage":"es","email":"someone.else@test.nexus.local"}""", false)]
    [InlineData("""{"preferredLanguage":"es","oid":"hijack"}""", false)]
    [InlineData("""not json""", false)]
    public async Task Invalid_body_is_400_and_changes_nothing(string body, bool fromValidator)
    {
        var user = await CreateUserAsync();
        using var client = _fixture.CreateClientFor(user.Oid);

        var response = await PatchRawAsync(client, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadAsync(response);
        if (fromValidator)
        {
            problem.GetProperty("code").GetString().ShouldBe("VALIDATION");
            problem.GetProperty("detail").GetString()!.ShouldContain("PreferredLanguage");
        }
        else
        {
            problem.GetProperty("code").GetString().ShouldBeOneOf("VALIDATION", "BAD_REQUEST");
        }

        (await Scalar<string>("SELECT preferred_language FROM org.app_user WHERE id = $1", user.Id)).ShouldBe("en");
        (await Scalar<string>("SELECT external_subject FROM org.app_user WHERE id = $1", user.Id)).ShouldBe(user.Oid);
        (await OutboxAsync(user.Id)).Select(e => e.EventType).ShouldBe(["org.user.created"]);
    }

    [Fact(DisplayName = "UI-ME: PATCH /v1/me without a token is 401")]
    public async Task Patch_without_a_token_is_401()
    {
        using var client = _fixture.CreateClient();

        var response = await PatchAsync(client, new { preferredLanguage = "es" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "ADR-0006: PATCH /v1/me for an oid without an app_user is 403 user_not_provisioned")]
    public async Task Patch_for_an_unprovisioned_oid_is_403()
    {
        using var client = _fixture.CreateClientFor(OrgFixture.UnprovisionedOid);

        var response = await PatchAsync(client, new { preferredLanguage = "es" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("user_not_provisioned");
    }

    /// <summary>
    /// SYSTEM never signs in (no <c>external_subject</c>), so to reach the endpoint as SYSTEM
    /// the test gives it one directly in the database (this class's own database; restored
    /// afterwards). The aggregate refuses before anything changes.
    /// </summary>
    [Fact(DisplayName = "ORG-R6: the SYSTEM user cannot change its language (422 ORG-R6, nothing written, no event)")]
    public async Task System_user_cannot_change_its_language()
    {
        const string systemOid = "oid-system-org-r6-test";
        await ExecuteAsync("UPDATE org.app_user SET external_subject = $1 WHERE id = $2", systemOid, OrgFixture.SystemUserId);
        try
        {
            var languageBefore = await Scalar<string>("SELECT preferred_language FROM org.app_user WHERE id = $1", OrgFixture.SystemUserId);
            var other = languageBefore == "fr" ? "it" : "fr";
            using var client = _fixture.CreateClientFor(systemOid);

            var response = await PatchAsync(client, new { preferredLanguage = other });

            response.StatusCode.ShouldBe((HttpStatusCode)422);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
            (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R6");
            (await Scalar<string>("SELECT preferred_language FROM org.app_user WHERE id = $1", OrgFixture.SystemUserId)).ShouldBe(languageBefore);
            (await Scalar<long>(
                "SELECT count(*) FROM outbox.message WHERE aggregate_id = $1 AND event_type = 'org.user.updated'",
                OrgFixture.SystemUserId)).ShouldBe(0);
        }
        finally
        {
            await ExecuteAsync("UPDATE org.app_user SET external_subject = NULL WHERE id = $1", OrgFixture.SystemUserId);
        }
    }

    /// <summary>
    /// Deterministic overlap, not a timing race (same technique as the warehouse ADR-0019 test):
    /// a superuser transaction holds the user's row <c>FOR UPDATE</c>; both PATCHes block on
    /// their own row lock (<c>FOR NO KEY UPDATE</c>, taken before loading); the test waits until
    /// both are seen waiting, then releases the row. Without that lock both would load the same
    /// <c>xmin</c>/<c>aggregate_version</c> and the second UPDATE would fail with
    /// DbUpdateConcurrencyException (409). With it they run one after the other: both 200, the
    /// final value is the last committed one, and the two events carry versions 2 and 3.
    /// </summary>
    [Fact(DisplayName = "UI-ME: two concurrent PATCHes of the same user both succeed, last write wins, versions 2 and 3, no 409")]
    public async Task Concurrent_patches_both_succeed_and_the_last_write_wins()
    {
        var user = await CreateUserAsync();
        using var client = _fixture.CreateClientFor(user.Oid);

        Task<HttpResponseMessage>[] patches = [];
        try
        {
            await using (var holder = await _fixture.Database.Admin.OpenConnectionAsync(Ct))
            await using (var hold = await holder.BeginTransactionAsync(Ct))
            {
                await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM org.app_user WHERE id = $1 FOR UPDATE", holder, hold))
                {
                    lockRow.Parameters.Add(new NpgsqlParameter { Value = user.Id });
                    (await lockRow.ExecuteScalarAsync(Ct)).ShouldNotBeNull();
                }

                patches =
                [
                    PatchAsync(client, new { preferredLanguage = "fr" }),
                    PatchAsync(client, new { preferredLanguage = "it" }),
                ];

                await WaitUntilAsync(
                    async () => await _fixture.Database.ScalarAsync<long>(
                        "SELECT count(*) FROM pg_stat_activity " +
                        "WHERE usename = 'svc_platform' AND wait_event_type = 'Lock' AND query ILIKE '%app_user%'") == 2,
                    patches);

                await hold.RollbackAsync(Ct);
            }

            var responses = await Task.WhenAll(patches);

            foreach (var response in responses)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            }

            (await ReadAsync(responses[0])).GetProperty("preferredLanguage").GetString().ShouldBe("fr");
            (await ReadAsync(responses[1])).GetProperty("preferredLanguage").GetString().ShouldBe("it");

            var events = await OutboxAsync(user.Id);
            events.Select(e => (e.Version, e.EventType)).ShouldBe(
            [
                (1L, "org.user.created"),
                (2L, "org.user.updated"),
                (3L, "org.user.updated"),
            ]);
            (await Scalar<long>("SELECT aggregate_version FROM org.app_user WHERE id = $1", user.Id)).ShouldBe(3);

            // Last write wins: the stored value is the one of the last committed event, and the
            // two events carry the two different requested values.
            var languages = events.Skip(1).Select(e => LanguageOf(e.Payload)).ToList();
            languages.ShouldBe(["fr", "it"], ignoreOrder: true);
            (await Scalar<string>("SELECT preferred_language FROM org.app_user WHERE id = $1", user.Id)).ShouldBe(languages[1]);
        }
        finally
        {
            // The row lock is released by now (the holder's transaction is disposed even when
            // WaitUntilAsync failed), so both PATCHes can finish: never leave them running.
            await Task.WhenAny(Task.WhenAll(patches));
            foreach (var patch in patches)
            {
                if (patch.IsCompletedSuccessfully)
                {
                    (await patch).Dispose();
                }
            }
        }
    }

    [Fact(DisplayName = "BE-R3: an admin PUT /v1/users/{id} with an ETag read before the user's PATCH is 409 CONCURRENCY")]
    public async Task Admin_put_with_an_etag_older_than_a_patch_is_409()
    {
        var user = await CreateUserAsync();
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var self = _fixture.CreateClientFor(user.Oid);
        var get = await admin.GetAsync(new Uri($"/v1/users/{user.Id}", UriKind.Relative), Ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var staleETag = get.Headers.ETag!.Tag;

        (await PatchAsync(self, new { preferredLanguage = "it" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var put = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/users/{user.Id}", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                oid = user.Oid,
                fullName = "Renamed " + user.Oid,
                email = (string?)null,
                preferredLanguage = "de",
                businessUnitId = Org.BusinessUnitA.Id,
            }),
        };
        put.Headers.TryAddWithoutValidation("If-Match", staleETag);
        var response = await admin.SendAsync(put, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("CONCURRENCY");
        (await Scalar<string>("SELECT preferred_language FROM org.app_user WHERE id = $1", user.Id)).ShouldBe("it");
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>A fresh active user in BU-A with language <c>en</c>, created by the GLOBAL admin.</summary>
    private async Task<TestUser> CreateUserAsync()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var tag = Guid.NewGuid().ToString("N");
        var oid = "oid-me-" + tag;
        var response = await admin.PostAsJsonAsync(
            new Uri("/v1/users", UriKind.Relative),
            new
            {
                oid,
                fullName = "Me Test " + tag,
                email = $"me.{tag}@test.nexus.local",
                preferredLanguage = "en",
                businessUnitId = Org.BusinessUnitA.Id,
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        return new TestUser(body.GetProperty("id").GetGuid(), body.GetProperty("partyId").GetGuid(), oid);
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, object body) =>
        client.PatchAsJsonAsync(Me, body, Ct);

    private static async Task<HttpResponseMessage> PatchRawAsync(HttpClient client, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.PatchAsync(Me, content, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private static string LanguageOf(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("preferredLanguage").GetString()!;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, IReadOnlyList<Task<HttpResponseMessage>> requests)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (requests.FirstOrDefault(r => r.IsCompleted) is { } done)
            {
                var response = await done;
                throw new ShouldAssertException(
                    $"A PATCH finished ({(int)response.StatusCode}) before both were blocked on the row lock: " +
                    await response.Content.ReadAsStringAsync(Ct));
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Both PATCHes never blocked on the app_user row lock.");
            }

            await Task.Delay(25, Ct);
        }
    }

    private Task<T?> Scalar<T>(string sql, params object[] parameters) => _fixture.Database.ScalarAsync<T>(sql, parameters);

    private async Task<uint> XminAsync(Guid id) =>
        (await Scalar<uint>("SELECT xmin FROM org.app_user WHERE id = $1", id))!;

    private async Task ExecuteAsync(string sql, params object[] parameters)
    {
        await using var command = _fixture.Database.Admin.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<OutboxRow>> OutboxAsync(Guid aggregateId)
    {
        await using var command = _fixture.Database.Admin.CreateCommand(
            "SELECT aggregate_version, event_type, topic, payload::text, (headers->>'actor_user_id')::uuid FROM outbox.message " +
            "WHERE aggregate_id = $1 ORDER BY aggregate_version, created_at");
        command.Parameters.Add(new NpgsqlParameter { Value = aggregateId });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<OutboxRow>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new OutboxRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetGuid(4)));
        }

        return rows;
    }

    private sealed record TestUser(Guid Id, Guid PartyId, string Oid);

    private sealed record OutboxRow(long Version, string EventType, string Topic, string Payload, Guid ActorUserId);
}
