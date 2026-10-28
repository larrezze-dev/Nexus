using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Nexus.BuildingBlocks.Web.Authorization;

namespace Nexus.BuildingBlocks.Web.OpenApi;

/// <summary>
/// Writes the `x-permission`/`x-scope`/`x-rules` OpenAPI extensions (conventions-api.md §3) onto
/// every generated operation, read back from the endpoint metadata that
/// <see cref="RequirePermissionExtensions"/> attaches:
/// <list type="bullet">
///   <item><see cref="PermissionMetadata"/> → `x-permission: "RESOURCE:ACTION"`.</item>
///   <item><see cref="PublicEndpointMetadata"/> → `x-permission: "PUBLIC"`.</item>
///   <item><see cref="AuthenticatedOnlyMetadata"/> → `x-permission: "AUTHENTICATED"`.</item>
///   <item><see cref="PermissionScopeMetadata"/> → `x-scope`.</item>
///   <item><see cref="PermissionRulesMetadata"/> → `x-rules` (only when non-empty).</item>
/// </list>
/// An endpoint with none of this metadata gets no `x-permission` extension at all.
/// </summary>
internal sealed class PermissionOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;

        var permission = endpointMetadata.OfType<PermissionMetadata>().FirstOrDefault()?.Permission
            ?? (endpointMetadata.OfType<PublicEndpointMetadata>().Any() ? "PUBLIC" : null)
            ?? (endpointMetadata.OfType<AuthenticatedOnlyMetadata>().Any() ? "AUTHENTICATED" : null);

        if (permission is null)
        {
            return Task.CompletedTask;
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-permission"] = new JsonNodeExtension(JsonValue.Create(permission));

        var scope = endpointMetadata.OfType<PermissionScopeMetadata>().FirstOrDefault();
        if (scope is not null)
        {
            operation.Extensions["x-scope"] = new JsonNodeExtension(JsonValue.Create(scope.Scope));
        }

        var rules = endpointMetadata.OfType<PermissionRulesMetadata>().FirstOrDefault();
        if (rules is { RuleIds.Count: > 0 })
        {
            var ruleArray = new JsonArray();
            foreach (var ruleId in rules.RuleIds)
            {
                ruleArray.Add(JsonValue.Create(ruleId));
            }

            operation.Extensions["x-rules"] = new JsonNodeExtension(ruleArray);
        }

        return Task.CompletedTask;
    }
}
