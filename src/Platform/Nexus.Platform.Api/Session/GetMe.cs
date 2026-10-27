using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Application;

namespace Nexus.Platform.Api.Session;

/// <summary>Handler for `GET /v1/me` (`x-permission: AUTHENTICATED`, conventions-api.md §3).</summary>
public static class GetMe
{
    /// <summary>JWT claim carrying the display name (Keycloak/Entra ID standard claim).</summary>
    private const string NameClaim = "name";

    /// <summary>JWT claim carrying the email (Keycloak/Entra ID standard claim).</summary>
    private const string EmailClaim = "email";

    /// <summary>JWT claim carrying the preferred UI language, when the IdP sends one.</summary>
    private const string LocaleClaim = "locale";

    /// <summary>Fallback `preferredLanguage` ("del token o `de`", ficha 0.5) when the token has
    /// no <see cref="LocaleClaim"/>.</summary>
    private const string DefaultPreferredLanguage = "de";

    /// <summary>
    /// Builds the <see cref="MeResponse"/> from the validated JWT. <see cref="ICurrentUser"/>
    /// only exposes <c>Oid</c>/<c>Subject</c>/<c>UserId</c> (always <see langword="null"/> in
    /// this step, BE-R1) - <paramref name="httpContext"/> is read directly for the other
    /// claims (`name`, `email`, `locale`) because they are not part of that interface yet.
    /// <list type="bullet">
    ///   <item><c>subject</c>/<c>oid</c> are required non-null strings in the contract; an
    ///   authenticated request always has at least a `sub` claim (the token would not have
    ///   validated otherwise), so the empty-string fallback only guards the (today impossible)
    ///   case of a missing `oid` claim.</item>
    ///   <item><c>displayName</c> is required too: it falls back to `subject` when the token
    ///   has no `name` claim.</item>
    /// </list>
    /// </summary>
    public static IResult Handle(HttpContext httpContext, ICurrentUser currentUser)
    {
        var principal = httpContext.User;

        var subject = currentUser.Subject ?? string.Empty;
        var oid = currentUser.Oid ?? string.Empty;
        var displayName = principal.FindFirst(NameClaim)?.Value ?? subject;
        var email = principal.FindFirst(EmailClaim)?.Value;
        var preferredLanguage = principal.FindFirst(LocaleClaim)?.Value ?? DefaultPreferredLanguage;

        var response = new MeResponse(
            Subject: subject,
            Oid: oid,
            DisplayName: displayName,
            Email: email,
            UserId: null,
            PartyId: null,
            BusinessUnitId: null,
            PreferredLanguage: preferredLanguage,
            Provisioned: false,
            Permissions: []);

        return Results.Ok(response);
    }
}
