using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Makes the request body re-readable for endpoints whose permission scope comes from the body
/// (<c>x-scope: body.*</c>). Needed because Minimal API endpoint filters
/// (<see cref="PermissionEndpointFilter"/>) run after parameter binding has already consumed
/// the body. Must run after routing (so the endpoint is known) and before the endpoint, which
/// is where WebApplication places any middleware added in Program.cs.
/// </summary>
public static class PermissionBodyBufferingMiddleware
{
    public static IApplicationBuilder UseNexusPermissionBodyBuffering(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(static (context, next) =>
        {
            var scope = context.GetEndpoint()?.Metadata.GetMetadata<PermissionScopeMetadata>();
            if (scope?.Scope.StartsWith("body.", StringComparison.Ordinal) == true)
            {
                context.Request.EnableBuffering();
            }

            return next(context);
        });
    }
}
