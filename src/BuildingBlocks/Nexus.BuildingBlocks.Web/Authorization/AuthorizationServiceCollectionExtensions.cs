using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Registers the pieces <see cref="RequirePermissionExtensions"/>/<see cref="PermissionEndpointFilter"/>
/// need: <see cref="NexusAuthorizationOptions"/> bound from configuration, and the default
/// <see cref="IPermissionChecker"/>. Does not call `AddAuthentication`/`AddAuthorization` -
/// that is the Authentication building block's responsibility (see its own
/// <c>AddNexusAuthentication</c>); this only adds the RBAC permission layer on top.
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

        // Step 0.6 registers the real access.has_permission-backed checker instead; until then,
        // this is harmless because EnforcePermissions defaults to false and PermissionEndpointFilter
        // never calls it unless that option is explicitly turned on.
        services.AddSingleton<IPermissionChecker, AlwaysAllowPermissionChecker>();

        return services;
    }
}
