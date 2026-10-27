using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Web.Authorization;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.Authorization;

public sealed class PermissionEndpointFilterTests
{
    private sealed class FakePermissionChecker : IPermissionChecker
    {
        public bool Allow { get; set; }

        public Task<bool> HasPermissionAsync(
            HttpContext httpContext,
            PermissionMetadata permission,
            PermissionScopeMetadata? scope,
            CancellationToken cancellationToken) => Task.FromResult(Allow);
    }

    private static (WebApplication App, FakePermissionChecker Checker) BuildApp(bool enforcePermissions)
    {
        var builder = WebApplication.CreateBuilder();
        var checker = new FakePermissionChecker();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{NexusAuthorizationOptions.SectionName}:EnforcePermissions"] = enforcePermissions.ToString(),
        });
        builder.Services.AddNexusAuthorization(builder.Configuration);
        builder.Services.AddSingleton<IPermissionChecker>(checker);

        var app = builder.Build();
        app.MapGet("/test", () => "ok")
            .RequirePermission("ASSET_TYPE", PermissionAction.Create);

        return (app, checker);
    }

    private static async Task<object?> InvokeAsync(WebApplication app)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .Single();

        var httpContext = new DefaultHttpContext { RequestServices = app.Services };
        httpContext.SetEndpoint(endpoint);

        var requestDelegate = endpoint.RequestDelegate;
        requestDelegate.ShouldNotBeNull();

        await requestDelegate(httpContext);

        return httpContext.Response.StatusCode;
    }

    [Fact]
    public async Task EnforcePermissions_False_Lets_Every_Call_Through()
    {
        var (app, checker) = BuildApp(enforcePermissions: false);
        checker.Allow = false; // would deny if it were ever asked

        var status = await InvokeAsync(app);

        status.ShouldBe(200);
    }

    [Fact]
    public async Task EnforcePermissions_True_And_Checker_Denies_Returns_403_Forbidden()
    {
        var (app, checker) = BuildApp(enforcePermissions: true);
        checker.Allow = false;

        var status = await InvokeAsync(app);

        status.ShouldBe(403);
    }

    [Fact]
    public async Task EnforcePermissions_True_And_Checker_Allows_Returns_200()
    {
        var (app, checker) = BuildApp(enforcePermissions: true);
        checker.Allow = true;

        var status = await InvokeAsync(app);

        status.ShouldBe(200);
    }

    [Fact]
    public void EnforcePermissions_Defaults_To_False()
    {
        new NexusAuthorizationOptions().EnforcePermissions.ShouldBeFalse();
    }
}
