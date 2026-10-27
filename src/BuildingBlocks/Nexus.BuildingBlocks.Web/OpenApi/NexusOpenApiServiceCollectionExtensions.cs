using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.BuildingBlocks.Web.OpenApi;

/// <summary>
/// Registers the OpenAPI document generation (<c>Microsoft.AspNetCore.OpenApi</c>) that every
/// NEXUS HTTP service exposes at <c>/openapi/{documentName}.json</c> (mapped with
/// <c>app.MapOpenApi()</c>), with <see cref="PermissionOpenApiOperationTransformer"/> wired in
/// so the generated document carries the same `x-permission`/`x-scope`/`x-rules` extensions as
/// the committed contract (conventions-api.md §3).
/// </summary>
public static class NexusOpenApiServiceCollectionExtensions
{
    /// <param name="documentName">The OpenAPI document name; also the <c>{documentName}</c>
    /// route segment once mapped with <c>app.MapOpenApi()</c>, e.g. <c>/openapi/v1.json</c> for
    /// the default <c>"v1"</c>.</param>
    public static IServiceCollection AddNexusOpenApi(this IServiceCollection services, string documentName = "v1")
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(documentName, options =>
        {
            options.AddOperationTransformer<PermissionOpenApiOperationTransformer>();
        });

        return services;
    }
}
