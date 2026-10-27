using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Web;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.Correlation;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Observability;
using Nexus.BuildingBlocks.Web.OpenApi;
using Nexus.Platform.Api.Session;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNexusApplication(typeof(Program).Assembly);
builder.Services.AddNexusWeb(builder.Configuration);
builder.Services.AddNexusProblemDetails();
// AddNexusWeb only registers authentication (AddNexusAuthentication); ASP.NET Core's
// authorization services are a separate registration every host must add itself (same as
// Nexus.Gateway's Program.cs) - AddNexusAuthorization (below) only adds the RBAC layer on top.
builder.Services.AddAuthorization();
builder.Services.AddNexusAuthorization(builder.Configuration);
builder.Services.AddNexusTelemetry(builder.Configuration, serviceName: "nexus-platform");
builder.Services.AddNexusOpenApi();

var app = builder.Build();

// Correlation first (every log line and error response of the request carries it), then
// ProblemDetails (so even an exception thrown by a later middleware is RFC 9457), then
// authentication/authorization.
app.UseNexusCorrelation();
app.UseNexusProblemDetails();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "up" }))
    .RequirePublic();

app.MapGet("/v1/me", GetMe.Handle)
    .RequireAuthenticatedOnly();

// Test-only diagnostic endpoint (ficha 0.5-B): exercises ResultExtensions.ToMinimalApiResult's
// 422 application/problem+json mapping end to end against a real running host, instead of only
// unit-testing it in isolation. Not part of the published contract: ExcludeFromDescription keeps
// it out of the generated OpenAPI document, so it never shows up in the B5 conformance/oasdiff
// check against contracts/openapi/platform.yaml.
app.MapGet(
        "/v1/_diagnostics/business-rule-error",
        () => Result.Failure(new Error("TST-R1", "test")).ToMinimalApiResult())
    .ExcludeFromDescription();

app.Run();

public partial class Program;
