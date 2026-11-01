using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.Correlation;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Identity;
using Nexus.BuildingBlocks.Web.Observability;
using Nexus.BuildingBlocks.Web.OpenApi;
using Nexus.Platform.Api.BusinessUnits;
using Nexus.Platform.Api.Currencies;
using Nexus.Platform.Api.Groups;
using Nexus.Platform.Api.LegalEntities;
using Nexus.Platform.Api.Locations;
using Nexus.Platform.Api.Parties;
using Nexus.Platform.Api.Session;
using Nexus.Platform.Api.Users;
using Nexus.Platform.Api.Warehouses;
using Nexus.Platform.Application.Users.CreateUser;
using Nexus.Platform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Handlers and validators live in Nexus.Platform.Application; the API assembly is scanned too.
builder.Services.AddNexusApplication(typeof(Program).Assembly, typeof(CreateUserCommand).Assembly);
builder.Services.AddNexusWeb(builder.Configuration);
builder.Services.AddNexusProblemDetails();
// AddNexusWeb only registers authentication (AddNexusAuthentication); ASP.NET Core's
// authorization services are a separate registration every host must add itself (same as
// Nexus.Gateway's Program.cs) - AddNexusAuthorization (below) only adds the RBAC layer on top.
builder.Services.AddAuthorization();
builder.Services.AddNexusAuthorization(builder.Configuration);
builder.Services.AddNexusTelemetry(builder.Configuration, serviceName: "nexus-platform");
builder.Services.AddNexusOpenApi();

// Persistence (svc_platform role, Nexus:Database) and the oid → org.app_user resolution that
// gives every authenticated request its ICurrentUser.UserId (step 0.4).
builder.Services.Configure<NexusPersistenceOptions>(builder.Configuration.GetSection(NexusPersistenceOptions.SectionName));
builder.Services.AddNexusPersistence();
builder.Services.AddNexusIdentityResolution();
builder.Services.AddPlatformInfrastructure();
builder.Services.AddUserConstraintViolations();
builder.Services.AddPartyConstraintViolations();
builder.Services.AddGroupConstraintViolations();
builder.Services.AddLegalEntityConstraintViolations();
builder.Services.AddBusinessUnitConstraintViolations();
builder.Services.AddWarehouseConstraintViolations();
builder.Services.AddLocationConstraintViolations();

var app = builder.Build();

// Correlation first (every log line and error response of the request carries it), then
// ProblemDetails (so even an exception thrown by a later middleware is RFC 9457), then
// authentication/authorization, then the NEXUS identity (after 401, before any endpoint).
app.UseNexusCorrelation();
app.UseNexusProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseNexusIdentityResolution();
// x-scope: body.* endpoints (POST /v1/role-assignments, ACC-R4): the permission filter runs
// after model binding has consumed the body, so it must be re-readable.
app.UseNexusPermissionBodyBuffering();

app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "up" }))
    .RequirePublic();

app.MapGet("/v1/me", GetMe.Handle)
    .WithName("getMe")
    .RequireAuthenticatedOnly()
    .WithScope("none");

app.MapUserEndpoints();
app.MapPartyEndpoints();
app.MapCurrencyEndpoints();
app.MapGroupEndpoints();
app.MapLegalEntityEndpoints();
app.MapBusinessUnitEndpoints();
app.MapWarehouseEndpoints();
app.MapLocationEndpoints();

// Test-only diagnostic endpoint (ficha 0.5-B): exercises ResultExtensions.ToMinimalApiResult's
// 422 application/problem+json mapping end to end against a real running host, instead of only
// unit-testing it in isolation. Not part of the published contract: ExcludeFromDescription keeps
// it out of the generated OpenAPI document, so it never shows up in the B5 conformance/oasdiff
// check against contracts/openapi/platform.yaml. Anonymous on purpose: it needs no database.
app.MapGet(
        "/v1/_diagnostics/business-rule-error",
        () => Result.Failure(new Error("TST-R1", "test")).ToMinimalApiResult())
    .ExcludeFromDescription();

// Test-only diagnostic endpoints (ficha 0.6-B): prove AccessPermissionChecker's x-scope handling
// end to end (JWT -> identity -> unit of work -> access.has_permission -> 200/403). Reaching the
// handler means the filter allowed the call. Excluded from the OpenAPI document like the one above.
app.MapGet("/v1/_diagnostics/permission-check/none", () => Results.Ok())
    .RequirePermission("ROLE", PermissionAction.Create)
    .WithScope("none")
    .ExcludeFromDescription();

app.MapGet("/v1/_diagnostics/permission-check/global", () => Results.Ok())
    .RequirePermission("ROLE", PermissionAction.Create)
    .WithScope("global")
    .ExcludeFromDescription();

// Same body shape and scope as POST /v1/role-assignments (ACC-R4); echoes the bound body to
// prove model binding still sees it after the filter re-read it.
app.MapPost(
        "/v1/_diagnostics/permission-check/role-assignment-scope",
        (DiagnosticRoleAssignmentScope body) => Results.Ok(body))
    .RequirePermission("ROLE_ASSIGNMENT", PermissionAction.Create)
    .WithScope("body.scopeEntityId")
    .ExcludeFromDescription();

app.Run();

public partial class Program;

/// <summary>Body of the role-assignment-scope diagnostic endpoint (test-only).</summary>
internal sealed record DiagnosticRoleAssignmentScope(string ScopeType, Guid? ScopeEntityId);
