using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Npgsql;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// ACC-R12 (at least one active user other than SYSTEM keeps a valid <c>GLOBAL</c>
/// <c>SYSTEM_ADMIN</c> assignment, checked under a lock) and ACC-R13 (nobody deactivates
/// themselves), end to end against the real host and database.
/// <para>
/// ACC-R12 is about the whole set of administrators, so every test first builds its own world:
/// it creates its users and their assignments directly in this class's own database (as the
/// superuser, attributed to SYSTEM), then ends - in the database, not through the API - every
/// other currently valid <c>SYSTEM_ADMIN</c> <c>GLOBAL</c> assignment of a non-SYSTEM user,
/// <see cref="OrgFixture.Admin"/>'s seeded one included (<see cref="KeepOnlyAdministratorsAsync"/>).
/// So the only qualifying administrators are the ones the test names, and the callers are
/// test-made users holding <c>SECURITY_ADMIN</c> or <c>ORG_ADMIN</c> at <c>GLOBAL</c> (both have
/// <c>ROLE_ASSIGNMENT:UPDATE</c>; <c>ORG_ADMIN</c> also <c>USER:UPDATE</c>/<c>USER:READ</c>),
/// which are not administrators themselves. Tests in a class run one after the other, so each
/// world is stable while its test runs; <see cref="OrgFixture.Admin"/> is not used as a caller.
/// </para>
/// </summary>
public sealed class AdministratorSafeguardApiTests : IClassFixture<PlatformApiFixture>
{
    private readonly PlatformApiFixture _fixture;

    public AdministratorSafeguardApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- ACC-R12: revoke --------------------------------------------------------------------

    [Fact(DisplayName = "ACC-R12: a GLOBAL SECURITY_ADMIN revoking the last qualifying administrator's GLOBAL SYSTEM_ADMIN assignment is 422 ACC-R12")]
    public async Task Revoking_the_last_administrators_assignment_is_rejected_ACC_R12()
    {
        var securityAdmin = await NewUserAsync();
        await GrantAsync(securityAdmin.Id, "SECURITY_ADMIN");
        var last = await NewUserAsync();
        var lastGrant = await GrantAsync(last.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(last.Id);
        using var client = _fixture.CreateClientFor(securityAdmin.Oid);

        var response = await RevokeAsync(client, lastGrant, await AssignmentETagAsync(lastGrant));

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R12");
        (await IsOpenAsync(lastGrant)).ShouldBeTrue();
        (await QualifyingAdministratorsAsync()).ShouldBe([last.Id]);
    }

    [Fact(DisplayName = "ACC-R12: with two qualifying administrators, revoking one's GLOBAL SYSTEM_ADMIN assignment is 200")]
    public async Task Revoking_one_of_two_administrators_succeeds_ACC_R12()
    {
        var securityAdmin = await NewUserAsync();
        await GrantAsync(securityAdmin.Id, "SECURITY_ADMIN");
        var first = await NewUserAsync();
        var firstGrant = await GrantAsync(first.Id, "SYSTEM_ADMIN");
        var second = await NewUserAsync();
        await GrantAsync(second.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(first.Id, second.Id);
        using var client = _fixture.CreateClientFor(securityAdmin.Oid);

        var response = await RevokeAsync(client, firstGrant, await AssignmentETagAsync(firstGrant));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await IsOpenAsync(firstGrant)).ShouldBeFalse();
        (await QualifyingAdministratorsAsync()).ShouldBe([second.Id]);
    }

    [Fact(DisplayName = "ACC-R12: counted per user - the last administrator holding two GLOBAL SYSTEM_ADMIN assignments loses one (200), not the other (422)")]
    public async Task Administrators_are_counted_per_user_ACC_R12()
    {
        var securityAdmin = await NewUserAsync();
        await GrantAsync(securityAdmin.Id, "SECURITY_ADMIN");
        var last = await NewUserAsync();
        var firstGrant = await GrantAsync(last.Id, "SYSTEM_ADMIN");
        var secondGrant = await GrantAsync(last.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(last.Id);
        using var client = _fixture.CreateClientFor(securityAdmin.Oid);

        (await RevokeAsync(client, firstGrant, await AssignmentETagAsync(firstGrant))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var response = await RevokeAsync(client, secondGrant, await AssignmentETagAsync(secondGrant));

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R12");
        (await IsOpenAsync(secondGrant)).ShouldBeTrue();
    }

    [Fact(DisplayName = "ACC-R12: the SYSTEM user does not count - its valid seeded GLOBAL SYSTEM_ADMIN assignment does not save the last administrator (422)")]
    public async Task System_user_does_not_count_ACC_R12()
    {
        var securityAdmin = await NewUserAsync();
        await GrantAsync(securityAdmin.Id, "SECURITY_ADMIN");
        var last = await NewUserAsync();
        var lastGrant = await GrantAsync(last.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(last.Id);

        // SYSTEM is active and holds a valid GLOBAL SYSTEM_ADMIN assignment (db/seed/common).
        (await Scalar<long>(
            "SELECT count(*) FROM access.role_assignment ra JOIN access.role r ON r.id = ra.role_id JOIN org.app_user u ON u.id = ra.user_id " +
            "WHERE ra.user_id = $1 AND r.code = 'SYSTEM_ADMIN' AND ra.scope_type = 'GLOBAL' AND ra.revoked_by IS NULL " +
            "AND ra.valid_from <= now() AND (ra.valid_to IS NULL OR ra.valid_to > now()) AND u.is_active",
            OrgFixture.SystemUserId)).ShouldBe(1);
        using var client = _fixture.CreateClientFor(securityAdmin.Oid);

        var response = await RevokeAsync(client, lastGrant, await AssignmentETagAsync(lastGrant));

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R12");
    }

    [Fact(DisplayName = "ACC-R12: an inactive user does not count - revoking their GLOBAL SYSTEM_ADMIN assignment is 200 even with a single qualifying administrator")]
    public async Task Revoking_an_inactive_users_assignment_is_not_blocked_ACC_R12()
    {
        var securityAdmin = await NewUserAsync();
        await GrantAsync(securityAdmin.Id, "SECURITY_ADMIN");
        var last = await NewUserAsync();
        await GrantAsync(last.Id, "SYSTEM_ADMIN");
        var inactive = await NewUserAsync();
        var inactiveGrant = await GrantAsync(inactive.Id, "SYSTEM_ADMIN");
        await ExecuteAsync("UPDATE org.app_user SET is_active = false WHERE id = $1", inactive.Id);
        await KeepOnlyAdministratorsAsync(last.Id, inactive.Id);
        using var client = _fixture.CreateClientFor(securityAdmin.Oid);

        var response = await RevokeAsync(client, inactiveGrant, await AssignmentETagAsync(inactiveGrant));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await QualifyingAdministratorsAsync()).ShouldBe([last.Id]);
    }

    [Fact(DisplayName = "ACC-R12: revoking the last administrator's already revoked assignment is still the idempotent 200, not 422")]
    public async Task Revoking_an_already_revoked_assignment_is_still_a_no_op_ACC_R12()
    {
        var securityAdmin = await NewUserAsync();
        await GrantAsync(securityAdmin.Id, "SECURITY_ADMIN");
        var last = await NewUserAsync();
        await GrantAsync(last.Id, "SYSTEM_ADMIN");
        var former = await NewUserAsync();
        var formerGrant = await GrantAsync(former.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(last.Id); // ends formerGrant
        using var client = _fixture.CreateClientFor(securityAdmin.Oid);
        var etag = await AssignmentETagAsync(formerGrant);

        var response = await RevokeAsync(client, formerGrant, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe(etag);
    }

    // --- ACC-R12: deactivate ----------------------------------------------------------------

    [Fact(DisplayName = "ACC-R12: deactivating the last qualifying administrator is 422 ACC-R12 (still active)")]
    public async Task Deactivating_the_last_administrator_is_rejected_ACC_R12()
    {
        var orgAdmin = await NewUserAsync();
        await GrantAsync(orgAdmin.Id, "ORG_ADMIN");
        var last = await NewUserAsync();
        await GrantAsync(last.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(last.Id);
        using var client = _fixture.CreateClientFor(orgAdmin.Oid);

        var response = await DeactivateAsync(client, last.Id, await UserETagAsync(client, last.Id));

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R12");
        (await Scalar<bool>("SELECT is_active FROM org.app_user WHERE id = $1", last.Id)).ShouldBeTrue();
    }

    [Fact(DisplayName = "ACC-R12: with two qualifying administrators, deactivating one is 200")]
    public async Task Deactivating_one_of_two_administrators_succeeds_ACC_R12()
    {
        var orgAdmin = await NewUserAsync();
        await GrantAsync(orgAdmin.Id, "ORG_ADMIN");
        var first = await NewUserAsync();
        await GrantAsync(first.Id, "SYSTEM_ADMIN");
        var second = await NewUserAsync();
        await GrantAsync(second.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(first.Id, second.Id);
        using var client = _fixture.CreateClientFor(orgAdmin.Oid);

        var response = await DeactivateAsync(client, first.Id, await UserETagAsync(client, first.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await QualifyingAdministratorsAsync()).ShouldBe([second.Id]);
    }

    [Fact(DisplayName = "ACC-R12: deactivating a user who is not an administrator is 200 even with a single qualifying administrator")]
    public async Task Deactivating_a_non_administrator_is_not_blocked_ACC_R12()
    {
        var orgAdmin = await NewUserAsync();
        await GrantAsync(orgAdmin.Id, "ORG_ADMIN");
        var last = await NewUserAsync();
        await GrantAsync(last.Id, "SYSTEM_ADMIN");
        var ordinary = await NewUserAsync();
        await KeepOnlyAdministratorsAsync(last.Id);
        using var client = _fixture.CreateClientFor(orgAdmin.Oid);

        var response = await DeactivateAsync(client, ordinary.Id, await UserETagAsync(client, ordinary.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact(DisplayName = "ACC-R12: deactivating an already inactive former administrator is still the idempotent 200, not 422")]
    public async Task Deactivating_an_inactive_user_is_still_a_no_op_ACC_R12()
    {
        var orgAdmin = await NewUserAsync();
        await GrantAsync(orgAdmin.Id, "ORG_ADMIN");
        var inactive = await NewUserAsync();
        await GrantAsync(inactive.Id, "SYSTEM_ADMIN");
        await ExecuteAsync("UPDATE org.app_user SET is_active = false WHERE id = $1", inactive.Id);
        await KeepOnlyAdministratorsAsync(inactive.Id); // nobody qualifies any more
        using var client = _fixture.CreateClientFor(orgAdmin.Oid);
        var etag = await UserETagAsync(client, inactive.Id);

        var response = await DeactivateAsync(client, inactive.Id, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe(etag);
    }

    // --- ACC-R12: concurrency ---------------------------------------------------------------

    /// <summary>
    /// Deterministic overlap, not a timing race (same technique as <c>UpdateMeEndpointTests</c>):
    /// a superuser transaction holds the <c>SYSTEM_ADMIN</c> row of <c>access.role</c>
    /// <c>FOR UPDATE</c>; both requests run up to the ACC-R12 lock (<c>FOR NO KEY UPDATE</c> on that
    /// row, taken before counting) and block there; the test waits until both are seen waiting,
    /// then releases the row. Whichever gets the lock first counts two administrators and
    /// succeeds; the other one counts afterwards, sees one, and is rejected. Without the lock both
    /// would count two and the set would end empty.
    /// </summary>
    [Theory(DisplayName = "ACC-R12 under concurrency: two simultaneous removals of the last two administrators - exactly one succeeds, the other is 422 ACC-R12")]
    [InlineData("deactivate+deactivate")]
    [InlineData("revoke+deactivate")]
    [InlineData("revoke+revoke")]
    public async Task Concurrent_removals_of_the_last_two_administrators_leave_one_ACC_R12(string mode)
    {
        var orgAdmin = await NewUserAsync(); // ORG_ADMIN GLOBAL: ROLE_ASSIGNMENT:UPDATE and USER:UPDATE
        await GrantAsync(orgAdmin.Id, "ORG_ADMIN");
        var first = await NewUserAsync();
        var firstGrant = await GrantAsync(first.Id, "SYSTEM_ADMIN");
        var second = await NewUserAsync();
        var secondGrant = await GrantAsync(second.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(first.Id, second.Id);
        using var client = _fixture.CreateClientFor(orgAdmin.Oid);

        // ETags read before the lock is held (GET /v1/users does not take it).
        var firstUserETag = await UserETagAsync(client, first.Id);
        var secondUserETag = await UserETagAsync(client, second.Id);
        var firstGrantETag = await AssignmentETagAsync(firstGrant);
        var secondGrantETag = await AssignmentETagAsync(secondGrant);

        Task<HttpResponseMessage>[] requests = [];
        try
        {
            await using (var holder = await _fixture.Database.Admin.OpenConnectionAsync(Ct))
            await using (var hold = await holder.BeginTransactionAsync(Ct))
            {
                await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM access.role WHERE code = 'SYSTEM_ADMIN' FOR UPDATE", holder, hold))
                {
                    (await lockRow.ExecuteScalarAsync(Ct)).ShouldNotBeNull();
                }

                requests = mode switch
                {
                    "deactivate+deactivate" =>
                    [
                        DeactivateAsync(client, first.Id, firstUserETag),
                        DeactivateAsync(client, second.Id, secondUserETag),
                    ],
                    "revoke+deactivate" =>
                    [
                        RevokeAsync(client, firstGrant, firstGrantETag),
                        DeactivateAsync(client, second.Id, secondUserETag),
                    ],
                    _ =>
                    [
                        RevokeAsync(client, firstGrant, firstGrantETag),
                        RevokeAsync(client, secondGrant, secondGrantETag),
                    ],
                };

                await WaitUntilAsync(
                    async () => await _fixture.Database.ScalarAsync<long>(
                        "SELECT count(*) FROM pg_stat_activity " +
                        "WHERE usename = 'svc_platform' AND wait_event_type = 'Lock' " +
                        "AND query ILIKE '%FROM access.role WHERE code%FOR NO KEY UPDATE%'") == 2,
                    requests);

                await hold.RollbackAsync(Ct);
            }

            var responses = await Task.WhenAll(requests);
            var statuses = responses.Select(r => (int)r.StatusCode).Order().ToList();
            statuses.ShouldBe([200, 422], string.Join(" | ", await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync(Ct)))));

            var rejected = responses.Single(r => (int)r.StatusCode == 422);
            (await ReadAsync(rejected)).GetProperty("code").GetString().ShouldBe("ACC-R12");

            // Exactly one qualifying administrator is left: whichever request lost.
            (await QualifyingAdministratorsAsync()).Count.ShouldBe(1);
        }
        finally
        {
            // The row lock is released by now (the holder's transaction is disposed even when
            // WaitUntilAsync failed), so both requests can finish: never leave them running.
            await Task.WhenAny(Task.WhenAll(requests));
            foreach (var request in requests)
            {
                if (request.IsCompletedSuccessfully)
                {
                    (await request).Dispose();
                }
            }
        }
    }

    // --- ACC-R13 ----------------------------------------------------------------------------

    [Fact(DisplayName = "ACC-R13: a user cannot deactivate themselves (422 ACC-R13, still active)")]
    public async Task Self_deactivation_is_rejected_ACC_R13()
    {
        var orgAdmin = await NewUserAsync();
        await GrantAsync(orgAdmin.Id, "ORG_ADMIN");
        using var client = _fixture.CreateClientFor(orgAdmin.Oid);

        var response = await DeactivateAsync(client, orgAdmin.Id, await UserETagAsync(client, orgAdmin.Id));

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R13");
        (await Scalar<bool>("SELECT is_active FROM org.app_user WHERE id = $1", orgAdmin.Id)).ShouldBeTrue();
    }

    [Fact(DisplayName = "ACC-R13 before ACC-R12: the last administrator deactivating themselves is 422 ACC-R13")]
    public async Task Self_deactivation_of_the_last_administrator_reports_ACC_R13()
    {
        var last = await NewUserAsync();
        await GrantAsync(last.Id, "SYSTEM_ADMIN");
        await KeepOnlyAdministratorsAsync(last.Id);
        using var client = _fixture.CreateClientFor(last.Oid);

        var response = await DeactivateAsync(client, last.Id, await UserETagAsync(client, last.Id));

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R13");
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>An active user with its <c>PERSON</c> party and a fresh <c>oid</c>, written as the superuser.</summary>
    private async Task<TestUser> NewUserAsync()
    {
        var tag = Guid.NewGuid().ToString("N");
        var user = new TestUser(Guid.CreateVersion7(), "oid-safeguard-" + tag);
        var partyId = Guid.CreateVersion7();
        await ExecuteAsync(
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, 'PERSON', $2, true, true, now(), $3, now(), $3)",
            partyId,
            "Safeguard " + tag,
            OrgFixture.SystemUserId);
        await ExecuteAsync(
            "INSERT INTO org.app_user (id, party_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, $3, $4, 'en', true, now(), $5, now(), $5)",
            user.Id,
            partyId,
            user.Oid,
            $"safeguard.{tag}@test.nexus.local",
            OrgFixture.SystemUserId);
        return user;
    }

    /// <summary>
    /// A <c>MANUAL</c> <c>GLOBAL</c> assignment of role <paramref name="roleCode"/>, written as the
    /// superuser and granted by SYSTEM. <c>valid_from</c> backdated a minute: the container's clock
    /// can lag the test host, and both <c>has_permission</c> (database <c>now()</c>) and ACC-R12
    /// (the host's <c>TimeProvider</c>) must see it as valid already.
    /// </summary>
    private async Task<Guid> GrantAsync(Guid userId, string roleCode)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            "INSERT INTO access.role_assignment (id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, " +
            "source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, (SELECT id FROM access.role WHERE code = $3), 'GLOBAL', NULL, now() - interval '1 minute', NULL, $4, 'MANUAL', " +
            "NULL, NULL, now(), $4, now(), $4)",
            id,
            userId,
            roleCode,
            OrgFixture.SystemUserId);
        return id;
    }

    /// <summary>
    /// Ends (<c>valid_to</c>, <c>revoked_by</c> = SYSTEM) every currently valid <c>SYSTEM_ADMIN</c>
    /// <c>GLOBAL</c> assignment of a non-SYSTEM user not in <paramref name="keep"/>, so those users
    /// are the only qualifying administrators (plus none, if they are inactive).
    /// </summary>
    private async Task KeepOnlyAdministratorsAsync(params Guid[] keep)
    {
        await ExecuteAsync(
            "UPDATE access.role_assignment ra SET valid_to = now(), revoked_by = $1, updated_at = now(), updated_by = $1 " +
            "FROM access.role r " +
            "WHERE r.id = ra.role_id AND r.code = 'SYSTEM_ADMIN' AND ra.scope_type = 'GLOBAL' AND ra.revoked_by IS NULL " +
            "AND ra.valid_from < now() AND (ra.valid_to IS NULL OR ra.valid_to > now()) " +
            "AND ra.user_id <> $1 AND NOT (ra.user_id = ANY($2))",
            OrgFixture.SystemUserId,
            keep);
    }

    /// <summary>The qualifying administrators (ACC-R12) as the database sees them now.</summary>
    private async Task<List<Guid>> QualifyingAdministratorsAsync()
    {
        await using var command = _fixture.Database.Admin.CreateCommand(
            "SELECT DISTINCT ra.user_id FROM access.role_assignment ra " +
            "JOIN access.role r ON r.id = ra.role_id JOIN org.app_user u ON u.id = ra.user_id " +
            "WHERE r.code = 'SYSTEM_ADMIN' AND r.is_active AND ra.scope_type = 'GLOBAL' AND ra.revoked_by IS NULL " +
            "AND ra.valid_from <= now() AND (ra.valid_to IS NULL OR ra.valid_to > now()) " +
            "AND u.is_active AND u.id <> $1");
        command.Parameters.Add(new NpgsqlParameter { Value = OrgFixture.SystemUserId });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(Ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<bool> IsOpenAsync(Guid assignmentId) =>
        await Scalar<bool>("SELECT valid_to IS NULL AND revoked_by IS NULL FROM access.role_assignment WHERE id = $1", assignmentId);

    private async Task<string> AssignmentETagAsync(Guid assignmentId) =>
        $"\"{await Scalar<long>("SELECT xmin::text::bigint FROM access.role_assignment WHERE id = $1", assignmentId)}\"";

    private static async Task<string> UserETagAsync(HttpClient client, Guid userId)
    {
        var response = await client.GetAsync(new Uri($"/v1/users/{userId}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response.Headers.ETag!.Tag;
    }

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid id, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/role-assignments/{id}/revoke", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid id, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/users/{id}/deactivate", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request, Ct);
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
                    $"A request finished ({(int)response.StatusCode}) before both were blocked on the ACC-R12 lock: " +
                    await response.Content.ReadAsStringAsync(Ct));
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Both requests never blocked on the ACC-R12 lock.");
            }

            await Task.Delay(25, Ct);
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private Task<T?> Scalar<T>(string sql, params object[] parameters) => _fixture.Database.ScalarAsync<T>(sql, parameters);

    private async Task ExecuteAsync(string sql, params object[] parameters)
    {
        await using var command = _fixture.Database.Admin.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private sealed record TestUser(Guid Id, string Oid);
}
