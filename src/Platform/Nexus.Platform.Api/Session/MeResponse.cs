namespace Nexus.Platform.Api.Session;

/// <summary>
/// Response body of `GET /v1/me` (`Me` schema, `contracts/openapi/platform.yaml`, version 0.4).
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
    IReadOnlyList<MePermissionResponse> Permissions);

/// <summary>One of the caller's permissions (`MePermission` schema): a code plus every scope
/// at which it is currently granted, one entry per distinct <see cref="Code"/>.</summary>
public sealed record MePermissionResponse(string Code, IReadOnlyList<MePermissionScopeResponse> Scopes);

/// <summary>A scope at which a permission is granted (`MePermissionScope` schema).
/// <see cref="ScopeEntityId"/> is <see langword="null"/> only when <see cref="ScopeType"/> is
/// <c>GLOBAL</c>.</summary>
public sealed record MePermissionScopeResponse(string ScopeType, Guid? ScopeEntityId);
