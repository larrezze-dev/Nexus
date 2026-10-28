using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Authentication;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests;

public sealed class NexusAuthenticationExtensionsTests
{
    private const string Authority = "http://localhost:8180/realms/nexus";

    [Fact]
    public void JwtBearer_IsConfiguredFromSection_WithFullValidationAndRawClaimNames()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Nexus:Authentication:Authority"] = Authority,
            ["Nexus:Authentication:RequireHttpsMetadata"] = "false",
        });

        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Authority.ShouldBe(Authority);
        options.Audience.ShouldBe("api://nexus");
        options.RequireHttpsMetadata.ShouldBeFalse();
        options.MapInboundClaims.ShouldBeFalse();
        options.TokenValidationParameters.ValidateIssuer.ShouldBeTrue();
        options.TokenValidationParameters.ValidateAudience.ShouldBeTrue();
        options.TokenValidationParameters.ValidateLifetime.ShouldBeTrue();
        options.TokenValidationParameters.ValidateIssuerSigningKey.ShouldBeTrue();
        options.TokenValidationParameters.RequireSignedTokens.ShouldBeTrue();
    }

    [Fact]
    public void Audience_CanBeOverridden_AndHttpsMetadataIsRequiredByDefault()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Nexus:Authentication:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
            ["Nexus:Authentication:Audience"] = "api://nexus-test",
        });

        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Audience.ShouldBe("api://nexus-test");
        options.RequireHttpsMetadata.ShouldBeTrue();
    }

    [Fact]
    public void MissingAuthority_FailsAtRegistration()
    {
        var configuration = new ConfigurationBuilder().Build();

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddNexusAuthentication(configuration));
    }

    [Fact]
    public void CurrentUserAndCorrelation_AreScoped_AndShareTheCorrelationInstance()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Nexus:Authentication:Authority"] = Authority,
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUser>().ShouldBeOfType<HttpCurrentUser>();
        var mutable = scope.ServiceProvider.GetRequiredService<CorrelationContext>();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().ShouldBeSameAs(mutable);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexusWeb(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
