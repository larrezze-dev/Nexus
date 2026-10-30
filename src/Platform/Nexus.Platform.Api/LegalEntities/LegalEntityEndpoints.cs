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
using Nexus.Platform.Application.LegalEntities;
using Nexus.Platform.Application.LegalEntities.CreateLegalEntity;
using Nexus.Platform.Application.LegalEntities.DeactivateLegalEntity;
using Nexus.Platform.Application.LegalEntities.GetLegalEntities;
using Nexus.Platform.Application.LegalEntities.GetLegalEntity;
using Nexus.Platform.Application.LegalEntities.UpdateLegalEntity;

namespace Nexus.Platform.Api.LegalEntities;

/// <summary>
/// <c>/v1/legal-entities</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag
/// LegalEntities). Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2):
/// body/route → command, <c>If-Match</c> → expected version, <c>Result</c> → status + <c>ETag</c>.
/// </summary>
public static class LegalEntityEndpoints
{
    public static RouteGroupBuilder MapLegalEntityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/legal-entities").WithTags("LegalEntities");

        group.MapGet(string.Empty, ListLegalEntities)
            .WithName("listLegalEntities")
            .ValidatedBy<GetLegalEntitiesQuery>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateLegalEntity)
            .WithName("createLegalEntity")
            .ValidatedBy<CreateLegalEntityCommand>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Create)
            .WithScope("none")
            .WithRules("ORG-R1", "ORG-R7");

        group.MapGet("/{id:guid}", GetLegalEntity)
            .WithName("getLegalEntity")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateLegalEntity)
            .WithName("updateLegalEntity")
            .ValidatedBy<UpdateLegalEntityCommand>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ORG-R7");

        group.MapPost("/{id:guid}/deactivate", DeactivateLegalEntity)
            .WithName("deactivateLegalEntity")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ORG-R5");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<LegalEntityListItemResponse>>, ProblemHttpResult>> ListLegalEntities(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetLegalEntitiesQuery(page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<LegalEntityListItemResponse>(
            value.Items
                .Select(le => new LegalEntityListItemResponse(le.Id, le.GroupId, le.Code, le.CurrencyCode, le.CountryCode, le.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<LegalEntityResponse>, Ok<LegalEntityResponse>, ProblemHttpResult>> CreateLegalEntity(
        CreateLegalEntityRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateLegalEntityCommand(
                request.Id,
                request.LegalName,
                request.DisplayName,
                request.GroupId,
                request.Code,
                request.CurrencyCode,
                request.CountryCode),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (legalEntity, created) = result.Value;
        response.SetETag(ToETag(legalEntity.Version));
        var body = LegalEntityResponse.From(legalEntity);

        return created
            ? TypedResults.Created($"/v1/legal-entities/{legalEntity.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<LegalEntityResponse>, ProblemHttpResult>> GetLegalEntity(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetLegalEntityQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(ToETag(result.Value.Version));
        return TypedResults.Ok(LegalEntityResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<LegalEntityResponse>, ProblemHttpResult>> UpdateLegalEntity(
        Guid id,
        UpdateLegalEntityRequest request,
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
            new UpdateLegalEntityCommand(id, expected, request.Code, request.CurrencyCode, request.CountryCode),
            cancellationToken);

        return ToLegalEntityResponse(result, response);
    }

    internal static async Task<Results<Ok<LegalEntityResponse>, ProblemHttpResult>> DeactivateLegalEntity(
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

        var result = await dispatcher.Send(new DeactivateLegalEntityCommand(id, expected), cancellationToken);
        return ToLegalEntityResponse(result, response);
    }

    private static Results<Ok<LegalEntityResponse>, ProblemHttpResult> ToLegalEntityResponse(
        Result<LegalEntityDetails> result,
        HttpResponse response)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(ToETag(result.Value.Version));
        return TypedResults.Ok(LegalEntityResponse.From(result.Value));
    }

    private static string ToETag(uint version) => version.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>If-Match</c> → expected version: absent → 428; present but not a version this API
    /// ever issued → 409 (it cannot match the current one, conventions-api.md §5).
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

        if (!uint.TryParse(token, CultureInfo.InvariantCulture, out expected))
        {
            problem = LegalEntityErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
