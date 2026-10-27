using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Web.ErrorHandling;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Enforces the permission attached by <see cref="RequirePermissionExtensions.RequirePermission"/>.
/// Authentication itself (401) is already handled upstream by `RequireAuthorization()` before
/// this filter ever runs; this filter only adds the 403 permission check, and only when
/// <see cref="NexusAuthorizationOptions.EnforcePermissions"/> is <see langword="true"/> (not
/// before step 0.6 - see that option's doc comment).
/// </summary>
internal static class PermissionEndpointFilter
{
    public static async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<NexusAuthorizationOptions>>()
            .Value;

        if (!options.EnforcePermissions)
        {
            return await next(context);
        }

        var permission = httpContext.GetEndpoint()?.Metadata.GetMetadata<PermissionMetadata>();
        if (permission is null)
        {
            // RequirePermission always attaches this metadata; a missing one means this filter
            // was added without it, which is a programming error, not a caller-facing 403/404.
            throw new InvalidOperationException(
                $"{nameof(PermissionEndpointFilter)} ran without a {nameof(PermissionMetadata)} on the endpoint.");
        }

        var scope = httpContext.GetEndpoint()?.Metadata.GetMetadata<PermissionScopeMetadata>();
        var checker = httpContext.RequestServices.GetRequiredService<IPermissionChecker>();

        var allowed = await checker.HasPermissionAsync(
            httpContext,
            permission,
            scope,
            httpContext.RequestAborted);

        if (!allowed)
        {
            var problemType = NexusProblemType.Forbidden;

            return Results.Problem(
                detail: $"Missing permission '{permission.Permission}'.",
                statusCode: problemType.Status,
                title: problemType.Title,
                type: problemType.Type,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = problemType.Code,
                    ["permission"] = permission.Permission,
                });
        }

        return await next(context);
    }
}
