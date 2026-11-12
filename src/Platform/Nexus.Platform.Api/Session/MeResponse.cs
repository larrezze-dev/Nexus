using Nexus.BuildingBlocks.Application;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Api.Session;

/// <summary>
/// Response body of `GET /v1/me` and `PATCH /v1/me` (`Me` schema, `contracts/openapi/platform.yaml`, version 0.4).
/// Property names are PascalCase here; the minimal API JSON defaults (`JsonSerializerDefaults.Web`)
/// camel-case them on the wire, matching the contract.
/// <para>
/// Since step 0.4 the caller is always resolved to an active <c>org.app_user</c> before this is
/// built (otherwise 403 <c>user_not_provisioned</c>): <see cref="UserId"/>, <see cref="PartyId"/>
/// and <see cref="PreferredLanguage"/> are always set and <see cref="Provisioned"/> is always
/// <see langword="true"/>; <see cref="BusinessUnitId"/> is optional in the data model.
/// Since step 0.6, <see cref="Permissions"/> carries the caller's real, currently granted
/// permissions (grouped by code, each with its scopes), read from <c>access.active_assignments()</c>
/// joined to <c>access.role_permission</c>/<c>access.permission</c>. The nullable types are kept
/// because the contract declares those properties as nullable.
/// </para>
/// </summary>
public sealed record MeResponse(
    string Subject,
    string Oid,
    string DisplayName,
    string? Email,
    Guid? UserId,
    Guid? PartyId,
    Guid? BusinessUnitId,
    string PreferredLanguage,
    bool Provisioned,
    IReadOnlyList<MePermissionResponse> Permissions)
{
    /// <summary>JWT claim carrying the display name (Keycloak/Entra ID standard claim).</summary>
    private const string NameClaim = "name";

    /// <summary>JWT claim carrying the email (Keycloak/Entra ID standard claim).</summary>
    private const string EmailClaim = "email";

    /// <summary>
    /// The one way both <c>/v1/me</c> operations build their body:
    /// <c>subject</c>/<c>oid</c>/<c>displayName</c>/<c>email</c> are token data (the contract:
    /// <c>email</c> "tomado del token, no de app_user"; <c>displayName</c> falls back to
    /// <c>subject</c> without a <c>name</c> claim); the rest is the <c>org.app_user</c> profile.
    /// Always <c>provisioned: true</c>: an unresolved caller never gets this far (ADR-0006).
    /// </summary>
    public static MeResponse From(HttpContext httpContext, ICurrentUser currentUser, CurrentUserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(profile);

        var principal = httpContext.User;
        var subject = currentUser.Subject ?? string.Empty;

        var permissions = profile.Permissions
            .Select(p => new MePermissionResponse(
                p.Code,
                p.Scopes.Select(s => new MePermissionScopeResponse(s.ScopeType, s.ScopeEntityId)).ToList()))
            .ToList();

        return new MeResponse(
            Subject: subject,
            Oid: currentUser.Oid ?? string.Empty,
            DisplayName: principal.FindFirst(NameClaim)?.Value ?? subject,
            Email: principal.FindFirst(EmailClaim)?.Value,
            UserId: profile.UserId,
            PartyId: profile.PartyId,
            BusinessUnitId: profile.BusinessUnitId,
            PreferredLanguage: profile.PreferredLanguage,
            Provisioned: true,
            Permissions: permissions);
    }
}

/// <summary>One of the caller's permissions (`MePermission` schema): a code plus every scope
/// at which it is currently granted, one entry per distinct <see cref="Code"/>.</summary>
public sealed record MePermissionResponse(string Code, IReadOnlyList<MePermissionScopeResponse> Scopes);

/// <summary>A scope at which a permission is granted (`MePermissionScope` schema).
/// <see cref="ScopeEntityId"/> is <see langword="null"/> only when <see cref="ScopeType"/> is
/// <c>GLOBAL</c>.</summary>
public sealed record MePermissionScopeResponse(string ScopeType, Guid? ScopeEntityId);
