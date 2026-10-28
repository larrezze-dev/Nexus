using System.Globalization;
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
    /// no <see cref="LocaleClaim"/>, or when it has one outside <see cref="SupportedLanguages"/>.</summary>
    private const string DefaultPreferredLanguage = "de";

    /// <summary>The exact `enum` the contract declares for `Me.preferredLanguage`
    /// (contracts/openapi/platform.yaml). An IdP-supplied locale that isn't a 2-letter
    /// prefix of one of these (e.g. Entra ID's `de-CH`, `en-US`, or an unsupported `pt`)
    /// must fall back to <see cref="DefaultPreferredLanguage"/> instead of leaking out of
    /// contract, since this response is never schema-validated at runtime.</summary>
    private static readonly HashSet<string> SupportedLanguages = ["de", "en", "fr", "it", "es"];

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
        var preferredLanguage = NormalizePreferredLanguage(principal.FindFirst(LocaleClaim)?.Value);

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

    /// <summary>
    /// Maps an IdP `locale` claim (e.g. `de-CH`, `en-US`, `pt`) to the contract's
    /// `preferredLanguage` enum: the lower-cased 2-letter language prefix if it is one of
    /// <see cref="SupportedLanguages"/>, otherwise <see cref="DefaultPreferredLanguage"/>.
    /// </summary>
    private static string NormalizePreferredLanguage(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return DefaultPreferredLanguage;
        }

        var prefix = locale.Length >= 2 ? locale[..2] : locale;
        var normalized = prefix.ToLower(CultureInfo.InvariantCulture);

        return SupportedLanguages.Contains(normalized) ? normalized : DefaultPreferredLanguage;
    }
}
