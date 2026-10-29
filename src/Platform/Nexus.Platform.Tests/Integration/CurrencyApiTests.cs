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
