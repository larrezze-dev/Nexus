using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Registers the pieces <see cref="RequirePermissionExtensions"/>/<see cref="PermissionEndpointFilter"/>
/// need: <see cref="NexusAuthorizationOptions"/> bound from configuration, and the
/// <c>access.has_permission</c>-backed <see cref="IPermissionChecker"/>
/// (<see cref="AccessPermissionChecker"/>). Does not call `AddAuthentication`/`AddAuthorization` -
/// that is the Authentication building block's responsibility (see its own
/// <c>AddNexusAuthentication</c>); this only adds the RBAC permission layer on top.
/// <para>
/// The checker depends on the scoped <c>IUnitOfWork</c>, so the host must also call
/// <c>AddNexusPersistence()</c>; and hosts with <c>x-scope: body.*</c> endpoints must call
/// <see cref="PermissionBodyBufferingMiddleware.UseNexusPermissionBodyBuffering"/>.
/// </para>
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddNexusAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<NexusAuthorizationOptions>()
            .Bind(configuration.GetSection(NexusAuthorizationOptions.SectionName));

        // Scoped, not singleton: it shares the request's scoped IUnitOfWork (the has_permission
        // call runs inside the request's own transaction). A singleton would capture one
        // request's unit of work forever.
        services.AddScoped<IPermissionChecker, AccessPermissionChecker>();

        return services;
    }
}
