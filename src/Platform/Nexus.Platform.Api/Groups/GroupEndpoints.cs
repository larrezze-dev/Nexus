using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Http;
using Nexus.Platform.Api.Users;
using Nexus.Platform.Application.Groups;
using Nexus.Platform.Application.Groups.CreateGroup;
using Nexus.Platform.Application.Groups.GetGroup;
using Nexus.Platform.Application.Groups.GetGroups;
using Nexus.Platform.Application.Groups.UpdateGroup;

namespace Nexus.Platform.Api.Groups;

/// <summary>
/// <c>/v1/groups</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag Groups).
/// Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2): body/route →
/// command, <c>If-Match</c> → expected version, <c>Result</c> → status + <c>ETag</c>.
/// </summary>
public static class GroupEndpoints
{
    public static RouteGroupBuilder MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/groups").WithTags("Groups");

        group.MapGet(string.Empty, ListGroups)
            .WithName("listGroups")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateGroup)
            .WithName("createGroup")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Create)
            .WithScope("none")
            .WithRules("ORG-R1", "ORG-R7");

        group.MapGet("/{id:guid}", GetGroup)
            .WithName("getGroup")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateGroup)
            .WithName("updateGroup")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ORG-R7");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<GroupListItemResponse>>, ProblemHttpResult>> ListGroups(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetGroupsQuery(page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<GroupListItemResponse>(
            value.Items
                .Select(g => new GroupListItemResponse(g.Id, g.PartyId, g.Code, g.ReportingCurrencyCode))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<GroupResponse>, Ok<GroupResponse>, ProblemHttpResult>> CreateGroup(
        CreateGroupRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateGroupCommand(
                request.Id,
                request.LegalName,
                request.DisplayName,
                request.Code,
                request.ConsolidationMethod,
                request.ReportingCurrencyCode),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (group, created) = result.Value;
        response.SetETag(ToETag(group.Version));
        var body = GroupResponse.From(group);

        return created
            ? TypedResults.Created($"/v1/groups/{group.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<GroupResponse>, ProblemHttpResult>> GetGroup(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetGroupQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(ToETag(result.Value.Version));
        return TypedResults.Ok(GroupResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<GroupResponse>, ProblemHttpResult>> UpdateGroup(
        Guid id,
        UpdateGroupRequest request,
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
            new UpdateGroupCommand(id, expected, request.Code, request.ConsolidationMethod, request.ReportingCurrencyCode),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(ToETag(result.Value.Version));
        return TypedResults.Ok(GroupResponse.From(result.Value));
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
            problem = GroupErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
