using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Application;

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

        // Same implementation, for handlers that check a loaded row's role-assignment scope
        // (e.g. revokeRoleAssignment). Always enforced, regardless of EnforcePermissions: it is
        // a handler-level rule (like ACC-R5/ACC-R6), not the endpoint filter, and it fails
        // closed. A separate registration (not a forward to IPermissionChecker) so a test that
        // swaps the filter's checker never silently swaps this one too.
        services.AddScoped<IRoleAssignmentScopeChecker, AccessPermissionChecker>();

        return services;
    }
}
