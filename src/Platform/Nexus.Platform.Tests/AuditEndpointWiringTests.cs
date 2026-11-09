using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Correlation;
using Nexus.BuildingBlocks.Web.Audit;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.OpenApi;
using Shouldly;

namespace Nexus.Platform.Tests;

/// <summary>
/// <c>AddNexusAudit</c> + <c>MapAuditEndpoint</c> are self-contained (conventions-api.md §7): a host
/// that does nothing else for audit serves its OpenAPI document and validates input, and a host that
/// forgets <c>AddNexusAudit</c> fails at startup, not on every request.
/// </summary>
public sealed class AuditEndpointWiringTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "conventions-api §7: a minimal host with only AddNexusAudit + MapAuditEndpoint serves /openapi and answers 400 to invalid input")]
    public async Task Minimal_host_serves_openapi_and_validates()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, AlwaysAuthenticatedHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddNexusAuthorization(builder.Configuration);
        builder.Services.Configure<NexusAuthorizationOptions>(o => o.EnforcePermissions = false);
        builder.Services.AddNexusOpenApi();
        builder.Services.Configure<NexusPersistenceOptions>(o => o.ConnectionString = "Host=localhost;Database=unused;Username=unused");
        builder.Services.AddSingleton<Nexus.BuildingBlocks.Application.ICurrentUser, StubCurrentUser>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddNexusApplication();
        builder.Services.AddNexusCorrelation();
        builder.Services.AddNexusPersistence();
        builder.Services.AddNexusProblemDetails();
        builder.Services.AddNexusAudit(builder.Configuration);

        await using var app = builder.Build();
        app.UseNexusProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOpenApi();
        app.MapAuditEndpoint();
        await app.StartAsync(Ct);

        using var client = app.GetTestClient();

        var document = await client.GetFromJsonAsync<JsonDocument>(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        document!.RootElement.GetProperty("paths").TryGetProperty("/v1/audit/{aggregateType}/{aggregateId}", out _).ShouldBeTrue();

        var response = await client.GetAsync(new Uri($"/v1/audit/Bad_Type/{Guid.NewGuid()}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;
        problem.GetProperty("code").GetString().ShouldBe("VALIDATION");
    }

    [Fact(DisplayName = "conventions-api §7: MapAuditEndpoint without AddNexusAudit fails fast at startup with a clear message")]
    public void Mapping_without_AddNexusAudit_fails_fast()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        using var app = builder.Build();

        var exception = Should.Throw<InvalidOperationException>(() => app.MapAuditEndpoint());
        exception.Message.ShouldContain("AddNexusAudit");
    }

    private sealed class StubCurrentUser : Nexus.BuildingBlocks.Application.ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? Oid => null;

        public string? Subject => null;

        public Guid? UserId => null;

        public Guid? PartyId => null;

        public Guid? BusinessUnitId => null;
    }

    private sealed class AlwaysAuthenticatedHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AlwaysAuthenticatedHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new System.Security.Claims.ClaimsIdentity("Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(identity), "Test")));
        }
    }
}
