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

        // CreateRoleAssignmentValidator / GetRoleAssignmentsValidator: MustBeOneOf(RoleAssignment.ScopeTypes).
        string[] scopeTypes = ["GROUP", "LEGAL_ENTITY", "BUSINESS_UNIT", "WAREHOUSE", "PROJECT", "GLOBAL"];
        schemas.GetProperty("CreateRoleAssignmentRequest").GetProperty("properties").GetProperty("scopeType")
            .GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(scopeTypes, ignoreOrder: true);
        root.GetProperty("paths").GetProperty("/v1/role-assignments").GetProperty("get")
            .GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "scopeType")
            .GetProperty("schema").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(scopeTypes, ignoreOrder: true);

        // Response-only number: written as a JSON number, not [integer, string].
        schemas.GetProperty("CurrencyResponse").GetProperty("properties").GetProperty("minorUnits")
            .GetProperty("type").GetString().ShouldBe("integer");
    }

    /// <summary>
    /// <c>contracts/openapi/_shared/audit.yaml</c> (conventions-api.md §7): the generic audit
    /// operation reaches the real document with the contract's id, permission, scope, parameter
    /// constraints and <c>AuditEvent</c> shape. The host starts with no ClickHouse configured.
    /// </summary>
    [Fact(DisplayName = "audit.yaml listAuditEvents: the generated document matches the shared contract fragment")]
    public async Task Generated_document_carries_the_audit_operation()
    {
        using var client = _fixture.CreateClient();

        var document = await client.GetFromJsonAsync<JsonDocument>(
            new Uri("/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var root = document!.RootElement;
        var operation = root.GetProperty("paths").GetProperty("/v1/audit/{aggregateType}/{aggregateId}").GetProperty("get");

        operation.GetProperty("operationId").GetString().ShouldBe("listAuditEvents");
        operation.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Audit"]);
        operation.GetProperty("x-permission").GetString().ShouldBe("AUDIT:READ");
        operation.GetProperty("x-scope").GetString().ShouldBe("none");

        var parameters = operation.GetProperty("parameters").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("schema"));
        parameters["aggregateType"].GetProperty("pattern").GetString().ShouldBe("^[a-z][a-z0-9]*([-_][a-z0-9]+)*$");
        parameters["aggregateId"].GetProperty("format").GetString().ShouldBe("uuid");
        parameters["page"].GetProperty("minimum").GetInt32().ShouldBe(1);
        parameters["pageSize"].GetProperty("maximum").GetInt32().ShouldBe(200);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var auditEvent = schemas.GetProperty("AuditEvent");
        auditEvent.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(
            ["eventId", "occurredAt", "eventType", "aggregateVersion", "actorUserId", "actorDisplayName", "correlationId", "payload", "previousPayload"],
            ignoreOrder: true);
        auditEvent.GetProperty("properties").GetProperty("occurredAt").GetProperty("type").GetString().ShouldBe("string");
        auditEvent.GetProperty("properties").GetProperty("payload").GetProperty("type").EnumerateArray()
            .Select(e => e.GetString()).ShouldBe(["object", "null"], ignoreOrder: true);
        schemas.GetProperty("AuditEventPage").GetProperty("properties").GetProperty("items")
            .GetProperty("items").GetProperty("$ref").GetString().ShouldBe("#/components/schemas/AuditEvent");
    }
}
