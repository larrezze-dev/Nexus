namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Endpoint metadata attached by <see cref="RequirePermissionExtensions.RequirePublic"/>: marks
/// an operation with the reserved `x-permission: PUBLIC` value (conventions-api.md §3) - no
/// authentication at all, e.g. `GET /health`. Read back by the OpenAPI operation transformer
/// (<c>Nexus.BuildingBlocks.Web.OpenApi</c>) to emit that extension.
/// </summary>
public sealed record PublicEndpointMetadata;

/// <summary>
/// Endpoint metadata attached by <see cref="RequirePermissionExtensions.RequireAuthenticatedOnly"/>:
/// marks an operation with the reserved `x-permission: AUTHENTICATED` value
/// (conventions-api.md §3) - any valid session is enough, there is no specific
/// `RESOURCE:ACTION` permission to check (e.g. `GET /v1/me`). Read back by the OpenAPI operation
/// transformer (<c>Nexus.BuildingBlocks.Web.OpenApi</c>) to emit that extension.
/// </summary>
public sealed record AuthenticatedOnlyMetadata;
