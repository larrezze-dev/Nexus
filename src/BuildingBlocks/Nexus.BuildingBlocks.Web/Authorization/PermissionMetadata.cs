namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Endpoint metadata attached by <see cref="RequirePermissionExtensions.RequirePermission"/>.
/// Read back in two places: <see cref="PermissionEndpointFilter"/> (to enforce it, once
/// <see cref="NexusAuthorizationOptions.EnforcePermissions"/> turns on in step 0.6) and the
/// OpenAPI operation transformer (to emit `x-permission`, conventions-api.md §3) - not shipped
/// in this step (see the Web building block's known-gaps note), but reads this same metadata.
/// </summary>
/// <param name="Resource">E.g. <c>"ASSET_TYPE"</c>.</param>
/// <param name="Action">E.g. <see cref="PermissionAction.Create"/>.</param>
public sealed record PermissionMetadata(string Resource, PermissionAction Action)
{
    /// <summary>The <c>RESOURCE:ACTION</c> token (conventions-backend.md §5), e.g. <c>"ASSET_TYPE:CREATE"</c>.</summary>
    public string Permission => $"{Resource}:{Action.ToString().ToUpperInvariant()}";
}

/// <summary>Endpoint metadata attached by <see cref="RequirePermissionExtensions.WithScope"/>:
/// where the permission's scope (legal entity / warehouse / project) comes from, e.g.
/// <c>"body.legalEntityId"</c>, <c>"path.projectId"</c>, <c>"none"</c>
/// (conventions-api.md §3).</summary>
public sealed record PermissionScopeMetadata(string Scope);

/// <summary>Endpoint metadata attached by <see cref="RequirePermissionExtensions.WithRules"/>:
/// the business-rule ids (`XXX-Rn`) this operation may return as a 422 `code`
/// (conventions-api.md §3).</summary>
public sealed record PermissionRulesMetadata(IReadOnlyList<string> RuleIds);
