using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.ErrorHandling;

/// <summary>
/// A minimal endpoint handler (<see cref="Handle"/>) that returns <c>Result.Failure(new
/// Error("TST-R1", ...))</c>, exercised end-to-end through <see cref="ResultExtensions"/> exactly
/// as a real Minimal API endpoint would: the <see cref="IResult"/> it returns is executed against
/// a real <see cref="DefaultHttpContext"/>, and the actual bytes written to the response are
/// parsed back as JSON. <see cref="RequestServices"/> comes from a real (unstarted)
/// <see cref="WebApplication"/> so <c>Results.Problem</c>/<c>Results.Ok</c> find the JSON options
/// they need to serialize, exactly as they would behind a real Minimal API host.
/// </summary>
public sealed class ResultExtensionsTests
{
    private static IServiceProvider RequestServices { get; } = WebApplication.CreateBuilder().Build().Services;

    private static IResult Handle() => Result.Failure(new Error("TST-R1", "Something went wrong."))
        .ToMinimalApiResult();

    [Fact]
    public async Task ToMinimalApiResult_On_Failure_Produces_422_Problem_Json_With_Rule_Code()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = RequestServices,
            Response = { Body = new MemoryStream() },
        };

        var result = Handle();
        await result.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.ShouldBe(422);
        httpContext.Response.ContentType.ShouldStartWith("application/problem+json");

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(
            httpContext.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
        var root = document.RootElement;

        root.GetProperty("status").GetInt32().ShouldBe(422);
        root.GetProperty("code").GetString().ShouldBe("TST-R1");
        root.GetProperty("detail").GetString().ShouldBe("Something went wrong.");
    }

    [Fact]
    public async Task ToMinimalApiResult_On_Success_Runs_The_OnSuccess_Branch()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = RequestServices,
            Response = { Body = new MemoryStream() },
        };

        var result = Result.Success(42).ToMinimalApiResult(value => Results.Ok(value));
        await result.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.ShouldBe(200);
    }
}
