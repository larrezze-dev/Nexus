namespace Nexus.Platform.Api.Session;

/// <summary>
/// Response body of `GET /v1/me` (`Me` schema, `contracts/openapi/platform.yaml`). Property
/// names are PascalCase here; the minimal API JSON defaults (`JsonSerializerDefaults.Web`)
/// camel-case them on the wire, matching the contract.
/// <para>
/// Step 0.5 only resolves what the validated JWT itself carries: <see cref="UserId"/>,
/// <see cref="PartyId"/> and <see cref="BusinessUnitId"/> stay <see langword="null"/> and
/// <see cref="Provisioned"/> stays <see langword="false"/> until 0.4-B's <c>oid -&gt; app_user</c>
/// resolution exists (the contract documents the same). <see cref="Permissions"/> stays empty
/// until step 0.6.
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
