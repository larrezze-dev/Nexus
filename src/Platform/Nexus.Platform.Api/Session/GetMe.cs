using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Session.GetCurrentUser;

namespace Nexus.Platform.Api.Session;

/// <summary>Handler for `GET /v1/me` (`x-permission: AUTHENTICATED`, conventions-api.md §3).</summary>
public static class GetMe
{
    /// <summary>JWT claim carrying the display name (Keycloak/Entra ID standard claim).</summary>
    private const string NameClaim = "name";

    /// <summary>JWT claim carrying the email (Keycloak/Entra ID standard claim).</summary>
    private const string EmailClaim = "email";

    /// <summary>
    /// Builds the <see cref="MeResponse"/> (contract version 0.4):
    /// <list type="bullet">
    ///   <item>By the time this runs, <c>IdentityResolutionMiddleware</c> has already resolved
    ///   the token's <c>oid</c> to an active <c>org.app_user</c>, or answered 403
    ///   <c>user_not_provisioned</c> without calling this handler (ADR-0006). So the response is
    ///   always <c>provisioned: true</c>, never 200 with <c>false</c>.</item>
    ///   <item><c>userId</c>/<c>partyId</c>/<c>businessUnitId</c>/<c>preferredLanguage</c> come
    ///   from <c>org.app_user</c>, read in a regular unit of work (<see cref="GetCurrentUserQuery"/>).
    ///   If the user was deactivated between the resolution and that read, the answer is the
    ///   same 403.</item>
    ///   <item><c>subject</c>/<c>oid</c>/<c>displayName</c>/<c>email</c> stay token data, as the
    ///   contract documents (<c>email</c>: "tomado del token, no de app_user");
    ///   <c>displayName</c> falls back to <c>subject</c> without a <c>name</c> claim.</item>
    ///   <item><c>permissions</c> stays empty until step 0.6.</item>
    /// </list>
    /// </summary>
    public static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> Handle(
        HttpContext httpContext,
        ICurrentUser currentUser,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (currentUser.UserId is null)
        {
            // Unreachable behind IdentityResolutionMiddleware; kept so the handler never
            // reports an unresolved caller as provisioned if the pipeline is ever miswired.
            return NexusProblems.UserNotProvisioned();
        }

        var profile = await dispatcher.Send(new GetCurrentUserQuery(), cancellationToken);
        if (profile.IsFailure)
        {
            return NexusProblems.UserNotProvisioned();
        }

        var principal = httpContext.User;
        var subject = currentUser.Subject ?? string.Empty;
        var me = profile.Value;

        return TypedResults.Ok(new MeResponse(
            Subject: subject,
            Oid: currentUser.Oid ?? string.Empty,
            DisplayName: principal.FindFirst(NameClaim)?.Value ?? subject,
            Email: principal.FindFirst(EmailClaim)?.Value,
            UserId: me.UserId,
            PartyId: me.PartyId,
            BusinessUnitId: me.BusinessUnitId,
            PreferredLanguage: me.PreferredLanguage,
            Provisioned: true,
            Permissions: []));
    }
}
