using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.ErrorHandling;

/// <summary>
/// 401/403/404/409/428 do not go through <c>NexusExceptionHandler</c> - they come from ASP.NET
/// Core's own authentication/authorization/routing middleware as an otherwise-empty response,
/// which <c>UseStatusCodePages()</c> turns into a problem via the
/// <c>CustomizeProblemDetails</c> callback registered by <c>AddNexusProblemDetails</c>. This
/// exercises that callback directly, without needing a running host.
/// </summary>
public sealed class NexusProblemDetailsServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(401, "UNAUTHORIZED")]
    [InlineData(403, "FORBIDDEN")]
    [InlineData(404, "NOT_FOUND")]
    [InlineData(409, "CONCURRENCY")]
    [InlineData(428, "PRECONDITION_REQUIRED")]
    public void CustomizeProblemDetails_Stamps_The_Right_Code_For_Each_Status(int status, string expectedCode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexusProblemDetails();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = status;

        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status };
        options.CustomizeProblemDetails!(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });

        problemDetails.Extensions["code"].ShouldBe(expectedCode);
    }

    [Fact]
    public void CustomizeProblemDetails_Does_Not_Override_An_Already_Set_Code()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexusProblemDetails();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = 422;

        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = 422 };
        problemDetails.Extensions["code"] = "TST-R1";

        options.CustomizeProblemDetails!(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });

        problemDetails.Extensions["code"].ShouldBe("TST-R1");
    }
}
