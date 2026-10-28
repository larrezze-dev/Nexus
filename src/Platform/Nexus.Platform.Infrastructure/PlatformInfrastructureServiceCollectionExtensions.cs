using Microsoft.Extensions.DependencyInjection;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Infrastructure.Parties;
using Nexus.Platform.Infrastructure.Persistence;
using Nexus.Platform.Infrastructure.Users;

namespace Nexus.Platform.Infrastructure;

/// <summary>DI wiring of <c>Nexus.Platform.Infrastructure</c>.</summary>
public static class PlatformInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OrgDbContext"/> and the repositories/read databases the application
    /// layer's ports need, all scoped (one per request, on that request's unit of work).
    /// Requires <c>AddNexusPersistence</c>.
    /// <para>
    /// <see cref="OrgDbContext"/> is registered directly, not with <c>AddDbContext</c>: its
    /// options are not configurable from outside on purpose (<c>DbContextBase</c> always binds
    /// it to the unit of work's connection, BE-R1).
    /// </para>
    /// </summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<OrgDbContext>();

        services.AddScoped<IPartyRepository, PartyRepository>();
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<IUserReadDb, UserReadDb>();

        return services;
    }
}
