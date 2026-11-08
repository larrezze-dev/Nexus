using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Nexus.BuildingBlocks.Analytics.Audit;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Authorization;

namespace Nexus.BuildingBlocks.Web.Audit;

/// <summary>
/// The generic audit endpoint every service mounts (conventions-api.md §7 <c>[+]</c>,
/// <c>contracts/openapi/_shared/audit.yaml</c>): <c>GET /v1/audit/{aggregateType}/{aggregateId}</c>.
/// Scope is enforced by the analytics query (<see cref="IAuditTrailReader"/>), hence
/// <c>x-scope: none</c> here; an unknown or invisible aggregate is a 200 with an empty page.
/// </summary>
public static partial class AuditEndpointExtensions
{
    internal const int MaxAggregateTypeLength = 64;

    public static RouteHandlerBuilder MapAuditEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.MapGet("/v1/audit/{aggregateType}/{aggregateId:guid}", ListAuditEvents)
            .WithName("listAuditEvents")
            .WithTags("Audit")
            .RequirePermission("AUDIT", PermissionAction.Read)
            .WithScope("none");
    }

    internal static async Task<Ok<AuditEventPage>> ListAuditEvents(
        string aggregateType,
        Guid aggregateId,
        IAuditTrailReader reader,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize)
    {
        Validate(aggregateType, page, pageSize);

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

    private static void Validate(string aggregateType, int page, int pageSize)
    {
        var failures = new List<ValidationFailure>();

        if (aggregateType.Length > MaxAggregateTypeLength || !AggregateTypeRegex().IsMatch(aggregateType))
        {
            failures.Add(new ValidationFailure("aggregateType", $"aggregateType must match ^[a-z][a-z0-9]*([-_][a-z0-9]+)*$ (max {MaxAggregateTypeLength} characters)."));
        }

        if (page < 1)
        {
            failures.Add(new ValidationFailure("page", "page must be greater than or equal to 1."));
        }

        if (pageSize is < 1 or > PageLimits.MaxPageSize)
        {
            failures.Add(new ValidationFailure("pageSize", $"pageSize must be between 1 and {PageLimits.MaxPageSize}."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    // Copied from the aggregateType path parameter of contracts/openapi/_shared/audit.yaml.
    [GeneratedRegex("^[a-z][a-z0-9]*([-_][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex AggregateTypeRegex();
}
