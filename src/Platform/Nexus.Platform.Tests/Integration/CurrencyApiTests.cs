using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// Rule ORG-R7 end to end: real host, real JWT, real PostgreSQL (all Flyway migrations +
/// <see cref="OrgFixture"/>), connecting as <c>svc_platform</c>.
/// <para>
/// This class shares one database across all its tests (<see cref="PlatformApiFixture"/> is a
/// class fixture). Tests that only exercise the negative path (blocked deactivation) leave no
/// trace. Tests that actually flip a currency's <c>isActive</c>, or reference one by creating a
/// legal entity, use USD or GBP (never seeded as referenced by anything) and are written to be
/// self-contained regardless of execution order: they establish their own precondition instead
/// of relying on another test having run first, and each currency is only ever mutated by tests
/// that agree on its end state (GBP ends inactive; USD ends referenced and active).
/// </para>
/// </summary>
public sealed class CurrencyApiTests : IClassFixture<PlatformApiFixture>
{
    private readonly PlatformApiFixture _fixture;

    public CurrencyApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ORG-R7: a currency used as a Group's reporting currency cannot be deactivated")]
    public async Task Deactivate_currency_used_as_group_reporting_currency_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (etag, _) = await GetCurrencyAsync(admin, "CHF");

        var response = await PutCurrencyAsync(admin, "CHF", etag, isActive: false);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R7");

        // Nothing changed: still active.
        (await GetCurrencyAsync(admin, "CHF")).IsActive.ShouldBeTrue();
    }

    [Fact(DisplayName = "ORG-R7: a currency used by an active legal entity cannot be deactivated")]
    public async Task Deactivate_currency_used_by_an_active_legal_entity_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (etag, _) = await GetCurrencyAsync(admin, "EUR");

        var response = await PutCurrencyAsync(admin, "EUR", etag, isActive: false);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R7");

        (await GetCurrencyAsync(admin, "EUR")).IsActive.ShouldBeTrue();
    }

    [Fact(DisplayName = "A currency used by nothing active can be deactivated")]
    public async Task Deactivate_an_unreferenced_currency_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        // GBP: never referenced by anything this class creates (USD is the one used to create a
        // legal entity in the positive create test, so it must stay out of this test). Self
        // contained: GBP may already be deactivated by another test in this class (the
        // precondition of the create-with-inactive-currency test also deactivates it), so make
        // sure it starts active before exercising the deactivation itself.
        await EnsureCurrencyActiveAsync(admin, "GBP");
        var (etag, _) = await GetCurrencyAsync(admin, "GBP");

        var response = await PutCurrencyAsync(admin, "GBP", etag, isActive: false);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await GetCurrencyAsync(admin, "GBP")).IsActive.ShouldBeFalse();
    }

    [Fact(DisplayName = "ORG-R7: creating a legal entity with an inactive currency fails")]
    public async Task Create_legal_entity_with_an_inactive_currency_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        await DeactivateCurrencyAsync(admin, "GBP");

        var response = await admin.PostAsJsonAsync(LegalEntities, NewLegalEntity(Org.GroupId, "GBP", "GB"), Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R7");
    }

    [Fact(DisplayName = "Creating a legal entity with an active currency succeeds")]
    public async Task Create_legal_entity_with_an_active_currency_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        // Self-contained: this test's whole point is the currency being active, regardless of
        // whether another test in this class already deactivated it.
        await EnsureCurrencyActiveAsync(admin, "USD");

        var response = await admin.PostAsJsonAsync(LegalEntities, NewLegalEntity(Org.GroupId, "USD", "US"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        body.GetProperty("currencyCode").GetString().ShouldBe("USD");
    }

    // --- create / rename (UI contract 2) ------------------------------------------------------

    [Fact(DisplayName = "createCurrency: 201 with Location and ETag, born active, and GET returns it")]
    public async Task Create_currency_returns_201_and_can_be_read_back()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await PostCurrencyAsync(admin, new { code = "SEK", captionI18n = new { de = "Schwedische Krone", en = "Swedish Krona" }, minorUnits = 2 });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldBe("/v1/currencies/SEK");
        response.Headers.ETag.ShouldNotBeNull();
        var body = await ReadAsync(response);
        body.GetProperty("code").GetString().ShouldBe("SEK");
        body.GetProperty("minorUnits").GetInt32().ShouldBe(2);
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        var get = await admin.GetAsync(response.Headers.Location, Ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var read = await ReadAsync(get);
        read.GetProperty("captionI18n").GetProperty("en").GetString().ShouldBe("Swedish Krona");
        get.Headers.ETag!.Tag.ShouldBe(response.Headers.ETag.Tag);
    }

    [Fact(DisplayName = "createCurrency: an existing code is a 409, also seeded ones")]
    public async Task Create_currency_with_an_existing_code_is_409()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var payload = new { code = "NOK", captionI18n = new { de = "Norwegische Krone" }, minorUnits = 2 };

        (await PostCurrencyAsync(admin, payload)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var duplicate = await PostCurrencyAsync(admin, payload);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(duplicate)).GetProperty("code").GetString().ShouldBe("CONFLICT");

        (await PostCurrencyAsync(admin, new { code = "CHF", captionI18n = new { de = "x" }, minorUnits = 2 }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory(DisplayName = "createCurrency: invalid shape is a 400 VALIDATION")]
    [InlineData("sek", "{\"de\":\"x\"}", 2)]
    [InlineData("SEKK", "{\"de\":\"x\"}", 2)]
    [InlineData("DKK", "{\"de\":\"x\"}", 5)]
    [InlineData("DKK", "{\"de\":\"x\"}", -1)]
    [InlineData("DKK", "{\"en\":\"x\"}", 2)]
    [InlineData("DKK", "{\"de\":\" \"}", 2)]
    [InlineData("SEK\\n", "{\"de\":\"x\"}", 2)]
    public async Task Create_currency_with_invalid_shape_is_400(string code, string captionJson, int minorUnits)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        using var content = new StringContent(
            $"{{\"code\":\"{code}\",\"captionI18n\":{captionJson},\"minorUnits\":{minorUnits}}}",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await admin.PostAsync(new Uri("/v1/currencies", UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("VALIDATION");
        (await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM org.currency WHERE code = $1", code)).ShouldBe(0);
    }

    [Fact(DisplayName = "createCurrency: a caller without CURRENCY:CREATE gets 403 (the grant is SYSTEM_ADMIN only)")]
    public async Task Create_currency_without_permission_is_403()
    {
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);

        var response = await PostCurrencyAsync(userA, new { code = "PLN", captionI18n = new { de = "Zloty" }, minorUnits = 2 });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM org.currency WHERE code = 'PLN'")).ShouldBe(0);
    }

    [Fact(DisplayName = "updateCurrency: captionI18n replaces the name with If-Match; code and minorUnits stay")]
    public async Task Update_currency_renames_and_keeps_code_and_minor_units()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await PostCurrencyAsync(admin, new { code = "CZK", captionI18n = new { de = "Tschechische Krone", en = "Czech Koruna" }, minorUnits = 3 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await PutAsync(admin, "CZK", created.Headers.ETag!.Tag, new { isActive = true, captionI18n = new { de = "Tschechische Krone (CZK)" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldNotBe(created.Headers.ETag.Tag);
        var body = await ReadAsync(response);
        body.GetProperty("code").GetString().ShouldBe("CZK");
        body.GetProperty("minorUnits").GetInt32().ShouldBe(3);
        var caption = body.GetProperty("captionI18n");
        caption.GetProperty("de").GetString().ShouldBe("Tschechische Krone (CZK)");
        caption.TryGetProperty("en", out _).ShouldBeFalse(); // full replacement

        var read = await ReadAsync(await admin.GetAsync(new Uri("/v1/currencies/CZK", UriKind.Relative), Ct));
        read.GetProperty("captionI18n").GetProperty("de").GetString().ShouldBe("Tschechische Krone (CZK)");
        read.GetProperty("minorUnits").GetInt32().ShouldBe(3);
    }

    [Fact(DisplayName = "updateCurrency: omitting captionI18n keeps the name; a caption without de is a 400; stale If-Match is 409")]
    public async Task Update_currency_without_caption_keeps_name_and_validates_caption()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await PostCurrencyAsync(admin, new { code = "HUF", captionI18n = new { de = "Forint" }, minorUnits = 2 });
        var etag = created.Headers.ETag!.Tag;

        var noCaption = await PutAsync(admin, "HUF", etag, new { isActive = false });
        noCaption.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(noCaption);
        body.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        body.GetProperty("captionI18n").GetProperty("de").GetString().ShouldBe("Forint");

        var badCaption = await PutAsync(admin, "HUF", noCaption.Headers.ETag!.Tag, new { isActive = true, captionI18n = new { en = "Forint" } });
        badCaption.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(badCaption)).GetProperty("code").GetString().ShouldBe("VALIDATION");

        var stale = await PutAsync(admin, "HUF", etag, new { isActive = true });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(stale)).GetProperty("code").GetString().ShouldBe("CONCURRENCY");
    }

    [Fact(DisplayName = "ORG-R7: renaming a currency still referenced does not block, but deactivating it still does")]
    public async Task Rename_of_a_referenced_currency_is_allowed_while_deactivation_stays_blocked()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (etag, _) = await GetCurrencyAsync(admin, "CHF");

        var rename = await PutAsync(admin, "CHF", etag, new { isActive = true, captionI18n = new { de = "Schweizer Franken", en = "Swiss Franc" } });
        rename.StatusCode.ShouldBe(HttpStatusCode.OK);

        var deactivate = await PutAsync(admin, "CHF", rename.Headers.ETag!.Tag, new { isActive = false, captionI18n = new { de = "Nope" } });
        deactivate.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(deactivate)).GetProperty("code").GetString().ShouldBe("ORG-R7");
        var read = await ReadAsync(await admin.GetAsync(new Uri("/v1/currencies/CHF", UriKind.Relative), Ct));
        read.GetProperty("captionI18n").GetProperty("de").GetString().ShouldBe("Schweizer Franken");
    }

    [Fact(DisplayName = "ORG-R7: renaming an already inactive currency that is still referenced is not blocked")]
    public async Task Rename_of_an_inactive_referenced_currency_is_not_blocked()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var created = await PostCurrencyAsync(admin, new { code = "AUD", captionI18n = new { de = "Australischer Dollar" }, minorUnits = 2 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        (await admin.PostAsJsonAsync(
            new Uri("/v1/groups", UriKind.Relative),
            new { legalName = "Aussie " + tag, code = "AU-" + tag, consolidationMethod = "FULL", reportingCurrencyCode = "AUD" },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Inactive while referenced can only be reached behind the API's back (ORG-R7 forbids it).
        await using (var owner = await _fixture.Database.OpenAsOwnerAsync(Ct))
        await using (var cmd = new Npgsql.NpgsqlCommand("UPDATE org.currency SET is_active = false WHERE code = 'AUD'", owner))
        {
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        var (etag, isActive) = await GetCurrencyAsync(admin, "AUD");
        isActive.ShouldBeFalse();

        var response = await PutAsync(admin, "AUD", etag, new { isActive = false, captionI18n = new { de = "Australischer Dollar (AUD)" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("captionI18n").GetProperty("de").GetString().ShouldBe("Australischer Dollar (AUD)");
    }

    private static Task<HttpResponseMessage> PostCurrencyAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(new Uri("/v1/currencies", UriKind.Relative), body, Ct);

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string code, string etag, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/currencies/{code}", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    // --- helpers ---------------------------------------------------------------------------

    private static readonly Uri LegalEntities = new("/v1/legal-entities", UriKind.Relative);

    private static object NewLegalEntity(Guid groupId, string currencyCode, string countryCode)
    {
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return new
        {
            legalName = "Test LE " + tag,
            groupId,
            code = "LE-" + tag,
            currencyCode,
            countryCode,
        };
    }

    private static async Task<(string ETag, bool IsActive)> GetCurrencyAsync(HttpClient client, string code)
    {
        var response = await client.GetAsync(new Uri($"/v1/currencies/{code}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        return (response.Headers.ETag!.Tag, body.GetProperty("isActive").GetBoolean());
    }

    private static async Task<HttpResponseMessage> PutCurrencyAsync(HttpClient client, string code, string etag, bool isActive)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/currencies/{code}", UriKind.Relative))
        {
            Content = JsonContent.Create(new { isActive }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request, Ct);
    }

    /// <summary>Deactivates <paramref name="code"/> if it is currently active; no-op otherwise.</summary>
    private static async Task DeactivateCurrencyAsync(HttpClient client, string code)
    {
        var (etag, isActive) = await GetCurrencyAsync(client, code);
        if (!isActive)
        {
            return;
        }

        var response = await PutCurrencyAsync(client, code, etag, isActive: false);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Activates <paramref name="code"/> if it is currently inactive; no-op otherwise.</summary>
    private static async Task EnsureCurrencyActiveAsync(HttpClient client, string code)
    {
        var (etag, isActive) = await GetCurrencyAsync(client, code);
        if (isActive)
        {
            return;
        }

        var response = await PutCurrencyAsync(client, code, etag, isActive: true);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;
}
