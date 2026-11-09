using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Nexus.BuildingBlocks.Web.Audit;

/// <summary>
/// Describes the two properties of <see cref="AuditEvent"/> the schema generator cannot express
/// on its own, exactly as <c>contracts/openapi/_shared/audit.yaml</c> does: <c>occurredAt</c> is a
/// <c>string</c> (<c>date-time</c>, written by <see cref="MicrosecondUtcConverter"/>) and
/// <c>payload</c>/<c>previousPayload</c> are <c>[object, "null"]</c> with free properties (a
/// <c>JsonElement</c> would otherwise be an empty schema).
/// </summary>
internal sealed class AuditEventOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        // Type level (after its properties were generated): the property-level wrapper of a
        // nullable JsonElement is not reachable from here, so rewrite the entries in place.
        if (context.JsonTypeInfo.Type != typeof(AuditEvent) || schema.Properties is not { } properties)
        {
            return Task.CompletedTask;
        }

        if (properties.TryGetValue("occurredAt", out _))
        {
            properties["occurredAt"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" };
        }

        foreach (var name in new[] { "payload", "previousPayload" })
        {
            if (properties.ContainsKey(name))
            {
                properties[name] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object | JsonSchemaType.Null,
                    AdditionalPropertiesAllowed = true,
                };
            }
        }

        return Task.CompletedTask;
    }
}
