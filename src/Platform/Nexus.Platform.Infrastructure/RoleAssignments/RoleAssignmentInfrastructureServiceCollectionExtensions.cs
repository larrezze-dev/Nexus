using Microsoft.Extensions.DependencyInjection;
using Nexus.Platform.Application.RoleAssignments;

namespace Nexus.Platform.Infrastructure.RoleAssignments;

/// <summary>
/// DI wiring of the <c>RoleAssignments</c> slice of <c>Nexus.Platform.Infrastructure</c>, kept in
/// its own extension method (rather than added to
/// <c>PlatformInfrastructureServiceCollectionExtensions.AddPlatformInfrastructure</c>) so this
/// feature and the parallel <c>Roles</c> feature (step 0.6-B) do not have to edit the same shared
/// file concurrently. Call <see cref="AddRoleAssignmentInfrastructure"/> from <c>Program.cs</c>
/// alongside <c>AddPlatformInfrastructure</c>.
/// </summary>
public static class RoleAssignmentInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RoleAssignmentDbContext"/> and the repository/read db/effective-scope
    /// reader the application layer's ports need, all scoped (one per request, on that request's
    /// unit of work) except nothing here needs to be a singleton: <see cref="EffectiveScopeReader"/>
    /// opens its own connection per call but is still fine as scoped (one instance per request is
    /// cheap, and it holds no state between calls). Requires <c>AddNexusPersistence</c> (for
    /// <c>ServiceDataSource</c>) and <c>AddPlatformInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddRoleAssignmentInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<RoleAssignmentDbContext>();
        services.AddScoped<IRoleAssignmentRepository, RoleAssignmentRepository>();
        services.AddScoped<IRoleAssignmentReadDb, RoleAssignmentReadDb>();
        services.AddScoped<IEffectiveScopeReader, EffectiveScopeReader>();

        return services;
    }
}
