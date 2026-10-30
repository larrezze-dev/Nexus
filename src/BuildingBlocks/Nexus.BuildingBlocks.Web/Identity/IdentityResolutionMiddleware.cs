using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Web.Authentication;
using Nexus.BuildingBlocks.Web.ErrorHandling;

namespace Nexus.BuildingBlocks.Web.Identity;

/// <summary>
/// Resolves the NEXUS user of every authenticated request (ADR-0006: claim <c>oid</c> →
/// <c>org.app_user.external_subject</c>, active users only), before any endpoint runs:
/// <list type="number">
///   <item>Skips requests that are not authenticated (the authorization middleware already
///   answered 401 where a token is required), requests that matched no endpoint (they end in
///   404) and endpoints that allow anonymous access (<c>/health</c>, the OpenAPI document): a
///   public endpoint never fails just because a token without an <c>app_user</c> came along.</item>
///   <item>Looks the <c>oid</c> up through <see cref="IAppUserDirectory"/>, on every request: no
///   cache, so a deactivation applies to the very next request (same policy as permissions,
///   conventions-backend.md §5).</item>
///   <item>Found: stores <see cref="AppUserIdentity"/> as a request feature; from then on
///   <c>ICurrentUser.UserId</c> is set and a unit of work can begin (BE-R1).</item>
///   <item>Not found (or no <c>oid</c> claim at all): ends the request with 403
///   <c>application/problem+json</c>, <c>code = "user_not_provisioned"</c>
///   (<c>contracts/openapi/platform.yaml</c>, <c>GET /v1/me</c>). The endpoint never runs.</item>
/// </list>
/// Place it after <c>UseAuthentication()</c>/<c>UseAuthorization()</c> (so 401 keeps precedence
/// and the endpoint is already selected) and before the endpoints themselves; see
/// <see cref="NexusIdentityResolutionExtensions.UseNexusIdentityResolution"/>.
/// <para>
/// Why a middleware and not an endpoint filter: it covers every protected endpoint by default
/// (deny by default), including ones a future step maps without <c>RequirePermission</c>, and it
/// runs before any endpoint filter, so the permission filter of step 0.6 can rely on
/// <c>ICurrentUser.UserId</c> being set.
/// </para>
/// </summary>
public sealed partial class IdentityResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public IdentityResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<IdentityResolutionMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        if (!RequiresResolution(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Resolved only here, not as an InvokeAsync parameter: anonymous requests (/health,
        // the OpenAPI document) must never need the database or its configuration.
        var directory = context.RequestServices.GetRequiredService<IAppUserDirectory>();
        var oid = context.User.FindFirst(JwtClaimNames.ObjectId)?.Value;
        var identity = string.IsNullOrWhiteSpace(oid)
            ? null
            : await directory.FindActiveByExternalSubjectAsync(oid, context.RequestAborted).ConfigureAwait(false);

        if (identity is null)
        {
            // No PII in logs (conventions-backend.md §8): neither the oid nor any claim value.
            LogNotProvisioned(logger);
            await WriteUserNotProvisionedAsync(context).ConfigureAwait(false);
            return;
        }

        context.Features.Set(identity);
        await _next(context).ConfigureAwait(false);
    }

    private static bool RequiresResolution(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var endpoint = context.GetEndpoint();
        return endpoint is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
    }

    private static Task WriteUserNotProvisionedAsync(HttpContext context) =>
        NexusProblems.UserNotProvisioned().ExecuteAsync(context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Authenticated caller has no active app_user; responding 403 user_not_provisioned.")]
    private static partial void LogNotProvisioned(ILogger logger);
}
