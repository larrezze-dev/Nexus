using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Nexus.BuildingBlocks.Web.OpenApi;

/// <summary>
/// Narrows the numeric properties of response-only types back to a plain JSON number (ADR-0014).
/// <para>
/// Minimal APIs serialize with <c>JsonSerializerDefaults.Web</c>, whose
/// <see cref="JsonNumberHandling.AllowReadingFromString"/> makes the schema generator describe
/// every number as <c>type: [integer, string]</c> plus a numeric-string <c>pattern</c>. That is
/// right for input (the service does accept <c>"5"</c>), but that flag only affects
/// <i>reading</i>: on output the service always writes a JSON number. For a property of a type
/// that only ever appears in responses, the schema therefore overstates what the service sends
/// and contradicts a contract that says <c>type: integer</c>.
/// </para>
/// Only narrowed when all of these hold, otherwise the schema is left as generated:
/// <list type="bullet">
///   <item>the schema is a property schema (not a parameter, not a whole body);</item>
///   <item>its declaring type is not reachable from any request body of the document;</item>
///   <item>its effective number handling does not include
///   <see cref="JsonNumberHandling.WriteAsString"/>;</item>
///   <item>its <c>pattern</c> is exactly the one the generator emits for quoted numbers.</item>
/// </list>
/// </summary>
internal sealed class ResponseNumberOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    // Patterns System.Text.Json's schema exporter emits for numbers readable from strings.
    private const string IntegerStringPattern = @"^-?(?:0|[1-9]\d*)$";
    private const string NumberStringPattern = @"^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$";

    private HashSet<Type>? _requestTypes;

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (context.ParameterDescription is not null ||
            context.JsonPropertyInfo is not { } property ||
            schema.Type is not { } type ||
            !type.HasFlag(JsonSchemaType.String) ||
            !(type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number)) ||
            schema.Pattern is not (IntegerStringPattern or NumberStringPattern))
        {
            return Task.CompletedTask;
        }

        var options = property.Options;
        var numberHandling = property.NumberHandling
            ?? options.GetTypeInfo(property.DeclaringType).NumberHandling
            ?? options.NumberHandling;
        if (numberHandling.HasFlag(JsonNumberHandling.WriteAsString))
        {
            return Task.CompletedTask;
        }

        _requestTypes ??= CollectRequestTypes(context.ApplicationServices);
        if (_requestTypes.Contains(property.DeclaringType))
        {
            return Task.CompletedTask;
        }

        schema.Type = type & ~JsonSchemaType.String;
        schema.Pattern = null;

        return Task.CompletedTask;
    }

    /// <summary>Every type reachable through JSON (properties, collection elements, dictionary
    /// keys/values) from any request body parameter of any endpoint.</summary>
    private static HashSet<Type> CollectRequestTypes(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var descriptions = services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups;

        var visited = new HashSet<Type>();
        var pending = new Stack<Type>();

        foreach (var description in descriptions.Items.SelectMany(group => group.Items))
        {
            foreach (var parameter in description.ParameterDescriptions)
            {
                if (parameter.Type is { } parameterType &&
                    (parameter.Source == BindingSource.Body || parameter.Source == BindingSource.Form))
                {
                    pending.Push(parameterType);
                }
            }
        }

        while (pending.TryPop(out var type))
        {
            if (!visited.Add(type))
            {
                continue;
            }

            JsonTypeInfo typeInfo;
            try
            {
                typeInfo = options.GetTypeInfo(type);
            }
            catch (NotSupportedException)
            {
                continue;
            }

            foreach (var jsonProperty in typeInfo.Properties)
            {
                pending.Push(jsonProperty.PropertyType);
            }

            if (typeInfo.ElementType is { } elementType)
            {
                pending.Push(elementType);
            }

            if (typeInfo.KeyType is { } keyType)
            {
                pending.Push(keyType);
            }

            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                pending.Push(underlying);
            }
        }

        return visited;
    }
}
