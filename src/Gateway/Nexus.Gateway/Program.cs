using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var authenticationConfig = builder.Configuration.GetSection("Nexus:Authentication");
var corsConfig = builder.Configuration.GetSection("Nexus:Cors");
var requestLimitsConfig = builder.Configuration.GetSection("Nexus:RequestLimits");
var rateLimitingConfig = builder.Configuration.GetSection("Nexus:RateLimiting");

var allowedOrigins = corsConfig.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
var documentsPathPrefix = requestLimitsConfig["DocumentsPathPrefix"] ?? "/api/platform/v1/documents";
var documentsMaxRequestBodySize = requestLimitsConfig.GetValue("DocumentsMaxRequestBodySize", 500L * 1024 * 1024);
var rateLimitPermitLimit = rateLimitingConfig.GetValue("PermitLimit", 100);
var rateLimitWindowSeconds = rateLimitingConfig.GetValue("WindowSeconds", 1);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ADR-0006: the gateway validates the JWT (issuer, audience, signature) itself, then forwards
// the original Authorization header so the downstream service (nexus-platform) validates it again.
var authority = authenticationConfig["Authority"];

// Local Keycloak runs over plain HTTP; Entra ID in Azure is always HTTPS. Derive the default
// from the configured Authority's own scheme (rather than ASPNETCORE_ENVIRONMENT, which
// `dotnet run` leaves as "Production" unless something sets it) so RequireHttpsMetadata never
// silently breaks a correctly-configured local Authority, and never silently relaxes a
// misconfigured https one.
var requireHttpsMetadataDefault =
    authority is null || authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = authenticationConfig["Audience"];
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = authenticationConfig.GetValue(
            "RequireHttpsMetadata",
            requireHttpsMetadataDefault);
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    // The web frontend (angular-auth-oidc-client) sends the access token as an Authorization
    // header, not a cookie, so the gateway does not need AllowCredentials here.
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Fixed window per authenticated user (claim "oid"); unauthenticated requests partition by
    // remote IP so one anonymous caller cannot starve others. 100 req/s per the 0.5 ficha.
    options.AddPolicy("per-user", httpContext =>
    {
        var oid = httpContext.User.FindFirst("oid")?.Value;
        var partitionKey = !string.IsNullOrEmpty(oid)
            ? $"user:{oid}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitPermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitWindowSeconds),
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

// Correlation id (ADR-0009: UUIDv7 generated in the application): reuse the inbound header when
// it is exactly one well-formed, non-empty GUID (same validation as
// Nexus.BuildingBlocks.Web.Correlation.CorrelationMiddleware, which this gateway does not
// reference to stay dependency-free) -- never echo an unvalidated client-supplied value back
// verbatim, or a caller could plant arbitrary text in both the forwarded request and the
// gateway's own response header (log/response injection, found live during review: a header
// like "<script>...> not-a-guid" was reflected unchanged). Anything else is discarded and
// replaced with a fresh one, same as a missing header.
app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-Id";

    var formatted = TryReadCorrelationId(context.Request.Headers[headerName]) is { } parsed
        ? parsed.ToString("D", CultureInfo.InvariantCulture)
        : Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture);

    context.Request.Headers[headerName] = formatted;

    context.Response.OnStarting(() =>
    {
        context.Response.Headers[headerName] = formatted;
        return Task.CompletedTask;
    });

    await next();
});

static Guid? TryReadCorrelationId(Microsoft.Extensions.Primitives.StringValues values)
{
    // Exactly one value: several X-Correlation-Id headers are ambiguous, so start a new flow.
    if (values.Count != 1)
    {
        return null;
    }

    return Guid.TryParse(values[0], CultureInfo.InvariantCulture, out var parsed) && parsed != Guid.Empty
        ? parsed
        : null;
}

// Body size: Kestrel enforces the default 10 MB limit from configuration
// (Kestrel:Limits:MaxRequestBodySize in appsettings.json). Raise it to 500 MB only for the
// documents upload endpoint, which nexus-platform does not implement yet (forward-looking,
// per the 0.5 ficha) but the gateway route must already tolerate once it exists.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(documentsPathPrefix, StringComparison.OrdinalIgnoreCase))
    {
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = documentsMaxRequestBodySize;
        }
    }

    await next();
});

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "up" }))
    .AllowAnonymous();

// YARP forwards all inbound request headers by default, including Authorization, to the
// destination cluster (ADR-0006) -- nothing removes it here.
app.MapReverseProxy()
    .RequireAuthorization()
    .RequireRateLimiting("per-user");

app.Run();

public partial class Program;
