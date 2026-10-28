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
}
