using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Nexus.BuildingBlocks.Testing;

/// <summary>
/// An in-memory host of <typeparamref name="TProgram"/> (a NEXUS HTTP service's
/// <c>Program</c>), wired so its JWT bearer authentication trusts <see cref="JwtIssuer"/>
/// instead of a real IdP: no Keycloak/Entra ID needed to exercise 401/200 behaviour in tests.
/// <para>
/// How: a <see cref="Microsoft.Extensions.Options.IPostConfigureOptions{TOptions}"/> added via
/// <c>ConfigureTestServices</c> runs after the host's own <c>AddNexusAuthentication</c> (options
/// post-configuration always runs in DI-registration order, and this fixture registers its
/// delegate on top of the already-built test host's services). It clears
/// <c>Authority</c>/<c>MetadataAddress</c>/<c>ConfigurationManager</c> - so
/// <c>JwtBearerHandler</c> never attempts OIDC discovery against the (non-existent, in tests)
/// <c>Authority</c> - and replaces <c>TokenValidationParameters</c> with static values that
/// validate exactly the tokens <see cref="JwtIssuer"/> issues.
/// </para>
/// </summary>
public class WebAppFixture<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    public TestJwtIssuer JwtIssuer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                // Cannot assign ConfigurationManager = null (non-nullable); a static,
                // already-"fetched" empty configuration makes JwtBearerHandler skip OIDC
                // discovery entirely instead of calling out to a (non-existent, in tests)
                // Authority - the explicit TokenValidationParameters below are what actually
                // validate the token.
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration());
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidIssuer = JwtIssuer.Issuer;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidAudience = JwtIssuer.Audience;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.IssuerSigningKey = JwtIssuer.SigningKey;
                options.TokenValidationParameters.ValidateLifetime = true;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            JwtIssuer.Dispose();
        }

        base.Dispose(disposing);
    }
}
