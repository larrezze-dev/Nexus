using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Http;
using Nexus.Platform.Api.Users;
using Nexus.Platform.Application.BusinessUnits;
using Nexus.Platform.Application.BusinessUnits.CreateBusinessUnit;
using Nexus.Platform.Application.BusinessUnits.DeactivateBusinessUnit;
using Nexus.Platform.Application.BusinessUnits.GetBusinessUnit;
using Nexus.Platform.Application.BusinessUnits.GetBusinessUnits;
using Nexus.Platform.Application.BusinessUnits.UpdateBusinessUnit;

namespace Nexus.Platform.Api.BusinessUnits;

/// <summary>
/// <c>/v1/business-units</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag
/// BusinessUnits). Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2).
/// </summary>
public static class BusinessUnitEndpoints
{
    public static RouteGroupBuilder MapBusinessUnitEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/business-units").WithTags("BusinessUnits");

        group.MapGet(string.Empty, ListBusinessUnits)
            .WithName("listBusinessUnits")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateBusinessUnit)
            .WithName("createBusinessUnit")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Create)
            .WithScope("none")
            .WithRules("ORG-R1");

        group.MapGet("/{id:guid}", GetBusinessUnit)
            .WithName("getBusinessUnit")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateBusinessUnit)
            .WithName("updateBusinessUnit")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none");

        group.MapPost("/{id:guid}/deactivate", DeactivateBusinessUnit)
            .WithName("deactivateBusinessUnit")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<BusinessUnitListItemResponse>>, ProblemHttpResult>> ListBusinessUnits(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetBusinessUnitsQuery(page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<BusinessUnitListItemResponse>(
            value.Items
                .Select(b => new BusinessUnitListItemResponse(b.Id, b.LegalEntityId, b.Code, b.CaptionI18n, b.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<BusinessUnitResponse>, Ok<BusinessUnitResponse>, ProblemHttpResult>> CreateBusinessUnit(
        CreateBusinessUnitRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateBusinessUnitCommand(
                request.Id,
                request.LegalName,
                request.DisplayName,
                request.LegalEntityId,
                request.Code,
                request.CaptionI18n,
                request.CostCenterCode),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (businessUnit, created) = result.Value;
        response.SetETag(businessUnit.Version.ToString(CultureInfo.InvariantCulture));
        var body = BusinessUnitResponse.From(businessUnit);

        return created
            ? TypedResults.Created($"/v1/business-units/{businessUnit.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<BusinessUnitResponse>, ProblemHttpResult>> GetBusinessUnit(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetBusinessUnitQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(BusinessUnitResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<BusinessUnitResponse>, ProblemHttpResult>> UpdateBusinessUnit(
        Guid id,
        UpdateBusinessUnitRequest request,
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
            new UpdateBusinessUnitCommand(id, expected, request.Code, request.CaptionI18n, request.CostCenterCode),
            cancellationToken);

        return ToResponse(result, response);
    }

    internal static async Task<Results<Ok<BusinessUnitResponse>, ProblemHttpResult>> DeactivateBusinessUnit(
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

        var result = await dispatcher.Send(new DeactivateBusinessUnitCommand(id, expected), cancellationToken);
        return ToResponse(result, response);
    }

    private static Results<Ok<BusinessUnitResponse>, ProblemHttpResult> ToResponse(Result<BusinessUnitDetails> result, HttpResponse response)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(BusinessUnitResponse.From(result.Value));
    }

    /// <summary>
    /// <c>If-Match</c> → expected version (the row's <c>xmin</c>): absent → 428; present but not
    /// a version this API ever issued → 409 (it cannot match the current one, conventions-api.md §5).
    /// </summary>
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
            problem = BusinessUnitErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
