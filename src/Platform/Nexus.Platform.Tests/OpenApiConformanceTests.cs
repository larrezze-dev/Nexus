using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests;

/// <summary>
/// Confirms <c>PermissionOpenApiOperationTransformer</c> actually runs against the real OpenAPI
/// document this host generates (<c>/openapi/v1.json</c>, mapped by <c>app.MapOpenApi()</c> in
/// `Program.cs`) - not just in a transformer-level unit test. A byte-for-byte comparison against
/// the committed `contracts/openapi/platform.yaml` (oasdiff) is a separate, later check (step
/// 0.5 track B5's CI conformance job), out of scope here.
/// </summary>
public sealed class OpenApiConformanceTests : IClassFixture<WebAppFixture<Program>>
{
    private readonly WebAppFixture<Program> _fixture;

    public OpenApiConformanceTests(WebAppFixture<Program> fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Generated_document_tags_health_as_public_and_me_as_authenticated()
    {
        using var client = _fixture.CreateClient();

        var document = await client.GetFromJsonAsync<JsonDocument>(
            new Uri("/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var paths = document!.RootElement.GetProperty("paths");

        paths.GetProperty("/health").GetProperty("get").GetProperty("x-permission").GetString()
            .ShouldBe("PUBLIC");
        paths.GetProperty("/v1/me").GetProperty("get").GetProperty("x-permission").GetString()
            .ShouldBe("AUTHENTICATED");

        // The test-only diagnostics endpoint must never leak into the generated document.
        paths.TryGetProperty("/v1/_diagnostics/business-rule-error", out _).ShouldBeFalse();
    }

    /// <summary>
    /// ADR-0014: the FluentValidation rules of the endpoints declared with <c>.ValidatedBy&lt;T&gt;()</c>
    /// reach the real document (one sample per projected rule kind), so the CI conformance job
    /// compares the service's actual constraints against <c>contracts/openapi/platform.yaml</c>.
    /// </summary>
    [Fact]
    public async Task Generated_document_carries_the_validator_constraints()
    {
        using var client = _fixture.CreateClient();

        var document = await client.GetFromJsonAsync<JsonDocument>(
            new Uri("/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var root = document!.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        // GetWarehousesValidator: GreaterThanOrEqualTo(1) / InclusiveBetween(1, MaxPageSize).
        var listParameters = root.GetProperty("paths").GetProperty("/v1/warehouses").GetProperty("get")
            .GetProperty("parameters").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("schema"));
        listParameters["page"].GetProperty("minimum").GetInt32().ShouldBe(1);
        listParameters["pageSize"].GetProperty("minimum").GetInt32().ShouldBe(1);
        listParameters["pageSize"].GetProperty("maximum").GetInt32().ShouldBe(200);

        // CreateWarehouseValidator: MustHaveGermanCaption().
        schemas.GetProperty("CreateWarehouseRequest").GetProperty("properties").GetProperty("captionI18n")
            .GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldContain("de");

        // CreateLegalEntityValidator: Matches(...).
        schemas.GetProperty("CreateLegalEntityRequest").GetProperty("properties").GetProperty("currencyCode")
            .GetProperty("pattern").GetString().ShouldBe("^[A-Z]{3}$");

        // CreateUserValidator: MustBeSupportedLanguage().
        schemas.GetProperty("CreateUserRequest").GetProperty("properties").GetProperty("preferredLanguage")
            .GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["de", "en", "fr", "it", "es"], ignoreOrder: true);

        // Response-only number: written as a JSON number, not [integer, string].
        schemas.GetProperty("CurrencyResponse").GetProperty("properties").GetProperty("minorUnits")
            .GetProperty("type").GetString().ShouldBe("integer");
    }
}
