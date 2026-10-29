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
/// <see cref="Permissions"/> stays empty until step 0.6. The nullable types are kept because the
/// contract declares those properties as nullable.
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
    IReadOnlyList<string> Permissions);
