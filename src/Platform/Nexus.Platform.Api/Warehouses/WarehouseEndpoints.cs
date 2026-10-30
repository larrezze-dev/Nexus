using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Http;
using Nexus.BuildingBlocks.Web.OpenApi;
using Nexus.Platform.Api.Users;
using Nexus.Platform.Application.Warehouses;
using Nexus.Platform.Application.Warehouses.CreateWarehouse;
using Nexus.Platform.Application.Warehouses.DeactivateWarehouse;
using Nexus.Platform.Application.Warehouses.GetWarehouse;
using Nexus.Platform.Application.Warehouses.GetWarehouses;
using Nexus.Platform.Application.Warehouses.UpdateWarehouse;

namespace Nexus.Platform.Api.Warehouses;

/// <summary>
/// <c>/v1/warehouses</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag
/// Warehouses). Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2).
/// </summary>
public static class WarehouseEndpoints
{
    public static RouteGroupBuilder MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/warehouses").WithTags("Warehouses");

        group.MapGet(string.Empty, ListWarehouses)
            .WithName("listWarehouses")
            .ValidatedBy<GetWarehousesQuery>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateWarehouse)
            .WithName("createWarehouse")
            .ValidatedBy<CreateWarehouseCommand>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Create)
            .WithScope("none")
            .WithRules("ORG-R3");

        group.MapGet("/{id:guid}", GetWarehouse)
            .WithName("getWarehouse")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateWarehouse)
            .WithName("updateWarehouse")
            .ValidatedBy<UpdateWarehouseCommand>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ORG-R3");

        group.MapPost("/{id:guid}/deactivate", DeactivateWarehouse)
            .WithName("deactivateWarehouse")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<WarehouseListItemResponse>>, ProblemHttpResult>> ListWarehouses(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null,
        string? q = null,
        Guid? legalEntityId = null,
        Guid? businessUnitId = null,
        bool? isActive = null)
    {
        var result = await dispatcher.Send(
            new GetWarehousesQuery(page, pageSize, sort, q, legalEntityId, businessUnitId, isActive), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<WarehouseListItemResponse>(
            value.Items
                .Select(w => new WarehouseListItemResponse(w.Id, w.LegalEntityId, w.BusinessUnitId, w.Code, w.CaptionI18n, w.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<WarehouseResponse>, Ok<WarehouseResponse>, ProblemHttpResult>> CreateWarehouse(
        CreateWarehouseRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateWarehouseCommand(request.Id, request.LocationId, request.LegalEntityId, request.BusinessUnitId, request.Code, request.CaptionI18n),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (warehouse, created) = result.Value;
        response.SetETag(warehouse.Version.ToString(CultureInfo.InvariantCulture));
        var body = WarehouseResponse.From(warehouse);

        return created
            ? TypedResults.Created($"/v1/warehouses/{warehouse.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<WarehouseResponse>, ProblemHttpResult>> GetWarehouse(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetWarehouseQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(WarehouseResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<WarehouseResponse>, ProblemHttpResult>> UpdateWarehouse(
        Guid id,
        UpdateWarehouseRequest request,
        IDispatcher dispatcher,
        HttpRequest httpRequest,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!TryReadExpectedVersion(httpRequest, out var expected, out var problem))
        {
            return problem;
        }

        var result = await dispatcher.Send(
            new UpdateWarehouseCommand(id, expected, request.LocationId, request.BusinessUnitId, request.Code, request.CaptionI18n),
            cancellationToken);

        return ToResponse(result, response);
    }

    internal static async Task<Results<Ok<WarehouseResponse>, ProblemHttpResult>> DeactivateWarehouse(
        Guid id,
        IDispatcher dispatcher,
        HttpRequest httpRequest,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!TryReadExpectedVersion(httpRequest, out var expected, out var problem))
        {
            return problem;
        }

        var result = await dispatcher.Send(new DeactivateWarehouseCommand(id, expected), cancellationToken);
        return ToResponse(result, response);
    }

    private static Results<Ok<WarehouseResponse>, ProblemHttpResult> ToResponse(Result<WarehouseDetails> result, HttpResponse response)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(WarehouseResponse.From(result.Value));
    }

    private static bool TryReadExpectedVersion(HttpRequest request, out uint expected, out ProblemHttpResult problem)
    {
        expected = default;
        problem = null!;

        if (!request.TryGetIfMatch(out var token))
        {
            problem = NexusProblems.PreconditionRequired();
            return false;
        }

        if (token is null || !uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out expected))
        {
            problem = WarehouseErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
