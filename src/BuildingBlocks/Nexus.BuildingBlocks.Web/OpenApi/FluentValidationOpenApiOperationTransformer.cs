using System.Runtime.CompilerServices;
using FluentValidation;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Nexus.BuildingBlocks.Web.OpenApi;

/// <summary>
/// Projects the FluentValidation rules that validate an endpoint's input onto its generated
/// OpenAPI operation (ADR-0014), so <c>/openapi/{documentName}.json</c> states the same
/// constraints the service enforces at runtime - <c>minimum</c>/<c>maximum</c>,
/// <c>pattern</c>, <c>enum</c>, required dictionary keys... (see
/// <see cref="ValidationRuleConstraints"/> for the full mapping and what is deliberately not
/// projected). Without it the document is built from the C# types alone and is looser than both
/// the service and the committed contract.
/// <para>
/// The validator comes from the endpoint's <see cref="ValidatedByMetadata"/>
/// (<c>.ValidatedBy&lt;TRequest&gt;()</c>), resolved as <c>IValidator&lt;TRequest&gt;</c> from DI
/// (the same lookup the dispatcher does). Each validated member is matched by name
/// (case-insensitive) first against the operation's path/query parameters, then against the
/// properties of the JSON request body schema. Inline schemas are modified in place; the request
/// body's component schema is modified through its reference, so a request DTO shared by
/// endpoints with <i>different</i> validators is rejected (the shared schema could only show one
/// of them).
/// </para>
/// An endpoint without <see cref="ValidatedByMetadata"/> is left untouched.
/// </summary>
internal sealed class FluentValidationOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    // Which validated type has already been projected onto a (shared) component schema. Weak so
    // a regenerated document never keeps the previous one's schemas alive.
    private static readonly ConditionalWeakTable<OpenApiSchema, Type> BodySchemaOwners = new();

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var declarations = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<ValidatedByMetadata>()
            .ToList();

        if (declarations.Count == 0)
        {
            return Task.CompletedTask;
        }

        var endpointName = context.Description.RelativePath ?? operation.OperationId ?? "(unnamed endpoint)";
        if (declarations.Select(d => d.ValidatedType).Distinct().Count() > 1)
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpointName}' declares more than one ValidatedBy<T>() type " +
                $"({string.Join(", ", declarations.Select(d => d.ValidatedType.Name))}); an endpoint dispatches one command/query.");
        }

        var validatedType = declarations[0].ValidatedType;
        var validatorType = typeof(IValidator<>).MakeGenericType(validatedType);
        if (context.ApplicationServices.GetService(validatorType) is not IValidator validator)
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpointName}' declares ValidatedBy<{validatedType.Name}>(), but no " +
                $"IValidator<{validatedType.Name}> is registered.");
        }

        var constraintsByMember = ValidationRuleConstraints.Read(validator);
        if (constraintsByMember.Count == 0)
        {
            return Task.CompletedTask;
        }

        var bodySchemas = ResolveBodySchemas(operation, validatedType, endpointName);

        foreach (var (member, constraints) in constraintsByMember)
        {
            if (TryFindParameterSchema(operation, member) is { } parameterSchema)
            {
                constraints.ApplyTo(parameterSchema);
                continue;
            }

            foreach (var bodySchema in bodySchemas)
            {
                if (TryFindPropertySchema(bodySchema, member) is { } propertySchema)
                {
                    constraints.ApplyTo(propertySchema);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema? TryFindParameterSchema(OpenApiOperation operation, string member)
    {
        if (operation.Parameters is null)
        {
            return null;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter is not OpenApiParameter { In: ParameterLocation.Query or ParameterLocation.Path } inline ||
                !string.Equals(inline.Name, member, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A $ref'd parameter schema is shared with other operations: leave it alone rather
            // than project this endpoint's rules onto every other user of it.
            return inline.Schema as OpenApiSchema;
        }

        return null;
    }

    private static OpenApiSchema? TryFindPropertySchema(OpenApiSchema bodySchema, string member)
    {
        if (bodySchema.Properties is null)
        {
            return null;
        }

        foreach (var (name, propertySchema) in bodySchema.Properties)
        {
            if (string.Equals(name, member, StringComparison.OrdinalIgnoreCase))
            {
                // Same as parameters: a $ref'd property schema is shared, leave it alone.
                return propertySchema as OpenApiSchema;
            }
        }

        return null;
    }

    private static List<OpenApiSchema> ResolveBodySchemas(OpenApiOperation operation, Type validatedType, string endpointName)
    {
        var result = new List<OpenApiSchema>();
        if (operation.RequestBody?.Content is null)
        {
            return result;
        }

        foreach (var mediaType in operation.RequestBody.Content.Values)
        {
            var schema = mediaType.Schema switch
            {
                OpenApiSchemaReference reference => reference.Target as OpenApiSchema,
                OpenApiSchema inline => inline,
                _ => null,
            };

            if (schema is null || result.Contains(schema))
            {
                continue;
            }

            var owner = BodySchemaOwners.GetValue(schema, _ => validatedType);
            if (owner != validatedType)
            {
                throw new InvalidOperationException(
                    $"Endpoint '{endpointName}' (ValidatedBy<{validatedType.Name}>) shares its request body schema " +
                    $"with an endpoint validated by {owner.Name}; the shared schema can only show one validator's " +
                    "rules. Give each endpoint its own request DTO.");
            }

            result.Add(schema);
        }

        return result;
    }
}
