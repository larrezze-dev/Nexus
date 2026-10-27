using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests;

/// <summary>
/// Exercises `ResultExtensions.ToMinimalApiResult`'s 422 mapping end to end against a real
/// running host (not just the unit tests already covering it in isolation in
/// `Nexus.BuildingBlocks.Web.Tests`). The endpoint under test
/// (`/v1/_diagnostics/business-rule-error`, `Program.cs`) exists only for this purpose and is
/// excluded from the generated OpenAPI document.
/// </summary>
public sealed class BusinessRuleErrorTests : IClassFixture<WebAppFixture<Program>>
{
    private readonly WebAppFixture<Program> _fixture;

    public BusinessRuleErrorTests(WebAppFixture<Program> fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Business_rule_failure_returns_422_problem_json_with_its_code()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/v1/_diagnostics/business-rule-error", UriKind.Relative),
            TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBe(422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        body!.RootElement.GetProperty("code").GetString().ShouldBe("TST-R1");
    }
}
