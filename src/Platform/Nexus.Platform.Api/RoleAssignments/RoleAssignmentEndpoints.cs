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
using Nexus.Platform.Application.RoleAssignments;
using Nexus.Platform.Application.RoleAssignments.CreateRoleAssignment;
using Nexus.Platform.Application.RoleAssignments.GetRoleAssignments;
using Nexus.Platform.Application.RoleAssignments.GetUserEffectiveScope;
using Nexus.Platform.Application.RoleAssignments.RevokeRoleAssignment;

namespace Nexus.Platform.Api.RoleAssignments;

/// <summary>
/// <c>/v1/role-assignments</c> and <c>/v1/users/{id}/effective-scope</c> (domain/access.md §4;
/// <c>contracts/openapi/platform.yaml</c>, tag RoleAssignments). Endpoints only translate HTTP ↔
/// commands/queries (conventions-backend.md §2).
/// </summary>
public static class RoleAssignmentEndpoints
{
    public static RouteGroupBuilder MapRoleAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/role-assignments").WithTags("RoleAssignments");

        group.MapGet(string.Empty, ListRoleAssignments)
            .WithName("listRoleAssignments")
            .ValidatedBy<GetRoleAssignmentsQuery>()
            .RequirePermission("ROLE_ASSIGNMENT", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateRoleAssignment)
            .WithName("createRoleAssignment")
            .ValidatedBy<CreateRoleAssignmentCommand>()
            .RequirePermission("ROLE_ASSIGNMENT", PermissionAction.Create)
            .WithScope("body.scopeEntityId")
            .WithRules("ACC-R1", "ACC-R2", "ACC-R4", "ACC-R5");

        group.MapPost("/{id:guid}/revoke", RevokeRoleAssignment)
            .WithName("revokeRoleAssignment")
            .RequirePermission("ROLE_ASSIGNMENT", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ACC-R3", "ACC-R5", "ACC-R6");

        app.MapGet("/v1/users/{id:guid}/effective-scope", GetUserEffectiveScope)
            .WithName("getUserEffectiveScope")
            .WithTags("RoleAssignments")
            .RequirePermission("ROLE_ASSIGNMENT", PermissionAction.Read)
            .WithScope("none");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<RoleAssignmentListItemResponse>>, ProblemHttpResult>> ListRoleAssignments(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null,
        Guid? userId = null,
        Guid? roleId = null,
        string? scopeType = null,
        DateTimeOffset? activeOn = null)
    {
        var result = await dispatcher.Send(
            new GetRoleAssignmentsQuery(page, pageSize, sort, userId, roleId, scopeType, activeOn), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<RoleAssignmentListItemResponse>(
            value.Items
                .Select(a => new RoleAssignmentListItemResponse(
                    a.Id, a.UserId, a.RoleId, a.ScopeType, a.ScopeEntityId, a.ValidFrom, a.ValidTo, a.Source))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<RoleAssignmentResponse>, Ok<RoleAssignmentResponse>, ProblemHttpResult>> CreateRoleAssignment(
        CreateRoleAssignmentRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateRoleAssignmentCommand(
                request.Id, request.UserId, request.RoleId, request.ScopeType, request.ScopeEntityId,
                request.ValidFrom, request.ValidTo),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (assignment, created) = result.Value;
        response.SetETag(assignment.Version.ToString(CultureInfo.InvariantCulture));
        var body = RoleAssignmentResponse.From(assignment);

        return created
            ? TypedResults.Created($"/v1/role-assignments/{assignment.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<RoleAssignmentResponse>, ProblemHttpResult>> RevokeRoleAssignment(
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

        var result = await dispatcher.Send(new RevokeRoleAssignmentCommand(id, expected), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(RoleAssignmentResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<EffectiveScopeResponse>, ProblemHttpResult>> GetUserEffectiveScope(
        Guid id,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetUserEffectiveScopeQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        return TypedResults.Ok(EffectiveScopeResponse.From(result.Value));
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
            problem = RoleAssignmentErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
