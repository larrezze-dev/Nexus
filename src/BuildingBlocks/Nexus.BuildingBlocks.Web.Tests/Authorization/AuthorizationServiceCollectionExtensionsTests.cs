using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Web.Authorization;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.Authorization;

public sealed class AuthorizationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNexusAuthorization_Binds_EnforcePermissions_From_Configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nexus:Authorization:EnforcePermissions"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddNexusAuthorization(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<NexusAuthorizationOptions>>().Value
            .EnforcePermissions.ShouldBeTrue();
    }

    [Fact]
    public void AddNexusAuthorization_Without_Configuration_Defaults_EnforcePermissions_To_False()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddNexusAuthorization(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<NexusAuthorizationOptions>>().Value
            .EnforcePermissions.ShouldBeFalse();
    }

    [Fact]
    public void AddNexusAuthorization_Registers_AccessPermissionChecker_As_Scoped()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddNexusAuthorization(configuration);

        // Scoped: it shares the request's scoped IUnitOfWork; a singleton would capture one.
        var descriptor = services.Single(d => d.ServiceType == typeof(IPermissionChecker));
        descriptor.ImplementationType.ShouldBe(typeof(AccessPermissionChecker));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }
}
