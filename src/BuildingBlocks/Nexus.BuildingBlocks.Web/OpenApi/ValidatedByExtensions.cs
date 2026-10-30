using Microsoft.AspNetCore.Builder;

namespace Nexus.BuildingBlocks.Web.OpenApi;

/// <summary>
/// Endpoint metadata attached by <see cref="ValidatedByExtensions.ValidatedBy{TRequest}"/>: the
/// command/query type whose <c>IValidator&lt;T&gt;</c> checks this endpoint's input, read by
/// <see cref="FluentValidationOpenApiOperationTransformer"/> (ADR-0014). Documentation only: it
/// never changes how the request is validated at runtime (that is the dispatcher's job).
/// </summary>
public sealed class ValidatedByMetadata
{
    public ValidatedByMetadata(Type validatedType)
    {
        ArgumentNullException.ThrowIfNull(validatedType);
        ValidatedType = validatedType;
    }

    /// <summary>The <c>T</c> of the <c>IValidator&lt;T&gt;</c> that validates this endpoint's input.</summary>
    public Type ValidatedType { get; }
}

/// <summary>
/// Declares which command/query validates a minimal-API endpoint's input, so the generated
/// OpenAPI document can show that validator's rules (ADR-0014):
/// <code>
/// group.MapGet(string.Empty, ListWarehouses)
///      .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
///      .ValidatedBy&lt;GetWarehousesQuery&gt;();
/// </code>
/// Needed because the endpoint method binds HTTP input (individual query/path parameters, an
/// API request DTO) and builds the command/query by hand; that mapping is invisible to the
/// OpenAPI generator, so the link to the validator has to be declared.
/// <para>
/// Contract of the declaration: every rule-carrying member of <typeparamref name="TRequest"/>
/// receives, <b>unchanged</b>, the value of the path/query parameter or JSON body property of
/// the same name (case-insensitive). A default applied when the input is absent is fine (the
/// projection only describes values that are present; it never marks a top-level input as
/// required); a member that is renamed or whose value is transformed on the way must not rely on
/// this projection - its rule would be shown on the wrong input.
/// </para>
/// </summary>
public static class ValidatedByExtensions
{
    public static RouteHandlerBuilder ValidatedBy<TRequest>(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new ValidatedByMetadata(typeof(TRequest)));

        return builder;
    }
}
