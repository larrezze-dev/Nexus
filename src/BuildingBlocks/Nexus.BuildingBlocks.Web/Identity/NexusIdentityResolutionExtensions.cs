using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nexus.BuildingBlocks.Web.Identity;

/// <summary>Registration and pipeline wiring of <see cref="IdentityResolutionMiddleware"/>.</summary>
public static class NexusIdentityResolutionExtensions
{
    /// <summary>
    /// Registers <see cref="IAppUserDirectory"/> over the service's own
    /// <c>ServiceDataSource</c>. Every business service uses this same mechanism, with its own
    /// role: all <c>svc_*</c> roles hold <c>SELECT</c> on <c>org</c> (a reference schema,
    /// ARC-R2), so no service needs <c>nexus-platform</c> to be up to know who its caller is.
    /// Requires <c>AddNexusPersistence</c> (it provides <c>ServiceDataSource</c>). Idempotent.
    /// </summary>
    public static IServiceCollection AddNexusIdentityResolution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAppUserDirectory, NpgsqlAppUserDirectory>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IdentityResolutionMiddleware"/>. Order in the pipeline:
    /// <code>
    /// app.UseNexusCorrelation();
    /// app.UseNexusProblemDetails();
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseNexusIdentityResolution();   // here: after 401, before any endpoint or filter
    /// </code>
    /// </summary>
    public static IApplicationBuilder UseNexusIdentityResolution(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<IdentityResolutionMiddleware>();
    }
}
