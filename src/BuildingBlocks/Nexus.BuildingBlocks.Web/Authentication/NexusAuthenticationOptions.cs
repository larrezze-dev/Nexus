namespace Nexus.BuildingBlocks.Web.Authentication;

/// <summary>
/// JWT bearer settings of a NEXUS service (ADR-0006), bound from the configuration section
/// <see cref="SectionName"/> (<c>Nexus:Authentication</c>):
/// <code>
/// "Nexus": {
///   "Authentication": {
///     "Authority": "http://localhost:8180/realms/nexus",   // Keycloak dev; Entra ID in Azure
///     "Audience": "api://nexus",
///     "RequireHttpsMetadata": false                        // only for local http Keycloak
///   }
/// }
/// </code>
/// Environment variables use the usual double underscore: <c>Nexus__Authentication__Authority</c>.
/// </summary>
public sealed class NexusAuthenticationOptions
{
    /// <summary>Configuration section the options are read from.</summary>
    public const string SectionName = "Nexus:Authentication";

    /// <summary>Default audience of every NEXUS API token (step 0.5).</summary>
    public const string DefaultAudience = "api://nexus";

    /// <summary>
    /// OIDC issuer. Its discovery document provides the signing keys and the expected
    /// <c>iss</c>. Required: there is no default, so a missing value fails at startup.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Expected <c>aud</c> claim. Defaults to <see cref="DefaultAudience"/>.</summary>
    public string Audience { get; set; } = DefaultAudience;

    /// <summary>
    /// Whether the discovery document must be fetched over HTTPS. Defaults to
    /// <see langword="true"/>; only the local Keycloak (plain http on port 8180) sets it to
    /// <see langword="false"/>.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
