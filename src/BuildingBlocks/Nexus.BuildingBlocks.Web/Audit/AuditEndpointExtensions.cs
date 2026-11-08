using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Analytics.Audit;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.OpenApi;

namespace Nexus.BuildingBlocks.Web.Audit;

/// <summary>
/// The generic audit endpoint every service mounts (conventions-api.md §7 <c>[+]</c>,
/// <c>contracts/openapi/_shared/audit.yaml</c>): <c>GET /v1/audit/{aggregateType}/{aggregateId}</c>.
/// Scope is enforced by the analytics query (<see cref="IAuditTrailReader"/>), hence
/// <c>x-scope: none</c> here; an unknown or invisible aggregate is a 200 with an empty page.
/// </summary>
public static class AuditEndpointExtensions
{
    public static RouteHandlerBuilder MapAuditEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ServiceProvider.GetService<AuditEndpointMarker>() is null)
        {
            throw new InvalidOperationException(
                "MapAuditEndpoint() requires services.AddNexusAudit(configuration) (after AddNexusPersistence) " +
                "in the host's service registration.");
        }

        return app.MapGet("/v1/audit/{aggregateType}/{aggregateId:guid}", ListAuditEvents)
            .WithName("listAuditEvents")
            .WithTags("Audit")
            .ValidatedBy<AuditTrailQuery>()
            .RequirePermission("AUDIT", PermissionAction.Read)
            .WithScope("none");
    }

    internal static async Task<Ok<AuditEventPage>> ListAuditEvents(
        string aggregateType,
        Guid aggregateId,
        IAuditTrailReader reader,
        IValidator<AuditTrailQuery> validator,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize)
    {
        var validation = await validator.ValidateAsync(new AuditTrailQuery(aggregateType, page, pageSize), cancellationToken);
        if (!validation.IsValid)
        {
            // errors[].field carries the API's camelCase parameter names, not the record's members.
            throw new ValidationException(validation.Errors.Select(failure => new ValidationFailure(
                char.ToLowerInvariant(failure.PropertyName[0]) + failure.PropertyName[1..],
                failure.ErrorMessage)));
        }

        // Already active (the permission filter began it); a no-op then.
        await unitOfWork.BeginAsync(cancellationToken);

        var result = await reader.ListAsync(aggregateType, aggregateId, page, pageSize, cancellationToken);
        var names = await AuditActorDirectory.ResolveAsync(unitOfWork, result.Items.Select(e => e.ActorUserId), cancellationToken);

        var items = result.Items
            .Select(e => new AuditEvent(
                e.EventId,
                e.OccurredAt,
                e.EventType,
                e.AggregateVersion,
                e.ActorUserId,
                names.GetValueOrDefault(e.ActorUserId),
                e.CorrelationId,
                e.Payload,
                e.PreviousPayload))
            .ToList();

        return TypedResults.Ok(new AuditEventPage(items, result.Page, result.PageSize, result.Total));
    }
}
