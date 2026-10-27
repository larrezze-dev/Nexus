using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Web.Correlation;

namespace Nexus.BuildingBlocks.Web.Authentication;

/// <summary>JWT bearer authentication and the HTTP <see cref="ICurrentUser"/> (ADR-0006).</summary>
public static class NexusAuthenticationExtensions
{
    /// <summary>
    /// Registers JWT bearer authentication as the default scheme, configured from
    /// <see cref="NexusAuthenticationOptions.SectionName"/> (<c>Nexus:Authentication</c>), and
    /// <see cref="HttpCurrentUser"/> as the scoped <see cref="ICurrentUser"/>.
    /// <list type="bullet">
    ///   <item>Issuer, audience, signature and lifetime validation stay on (the
    ///   <see cref="JwtBearerOptions"/> defaults); this method never relaxes them.</item>
    ///   <item><see cref="JwtBearerOptions.MapInboundClaims"/> = <see langword="false"/>: claims
    ///   keep their JWT names (<c>oid</c>, <c>sub</c>) instead of the legacy
    ///   <c>http://schemas.xmlsoap.org/...</c> URIs.</item>
    /// </list>
    /// Throws <see cref="InvalidOperationException"/> at registration time when
    /// <c>Authority</c> or <c>Audience</c> is missing.
    /// </summary>
    public static IServiceCollection AddNexusAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection(NexusAuthenticationOptions.SectionName).Get<NexusAuthenticationOptions>()
            ?? new NexusAuthenticationOptions();

        if (string.IsNullOrWhiteSpace(settings.Authority))
        {
            throw new InvalidOperationException(
                $"Missing configuration '{NexusAuthenticationOptions.SectionName}:Authority' (OIDC issuer, ADR-0006).");
        }

        if (string.IsNullOrWhiteSpace(settings.Audience))
        {
            throw new InvalidOperationException(
                $"Configuration '{NexusAuthenticationOptions.SectionName}:Audience' must not be empty.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority;
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                options.MapInboundClaims = false;

                // Explicit, so that nobody "simplifies" them away: these are the defaults.
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.RequireSignedTokens = true;
                options.TokenValidationParameters.RequireExpirationTime = true;
                options.TokenValidationParameters.NameClaimType = JwtClaimNames.Subject;
            });

        services.AddHttpContextAccessor();
        services.AddNexusCorrelation(); // HttpCurrentUser exposes the request's correlation id.
        services.TryAddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }
}
