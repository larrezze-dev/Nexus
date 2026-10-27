namespace Nexus.BuildingBlocks.Web.Authentication;

/// <summary>
/// Raw JWT claim names as they reach the <see cref="System.Security.Claims.ClaimsPrincipal"/>
/// with <c>MapInboundClaims = false</c> (no mapping to <c>ClaimTypes.*</c> URIs).
/// </summary>
public static class JwtClaimNames
{
    /// <summary>Entra ID object id of the user (ADR-0006); emitted by a mapper in Keycloak.</summary>
    public const string ObjectId = "oid";

    /// <summary>Standard JWT subject (RFC 7519 §4.1.2).</summary>
    public const string Subject = "sub";
}
