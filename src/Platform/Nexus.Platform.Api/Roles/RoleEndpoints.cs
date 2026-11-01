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
using Nexus.Platform.Application.Permissions;
using Nexus.Platform.Application.Permissions.GetPermissions;
using Nexus.Platform.Application.Roles;
using Nexus.Platform.Application.Roles.CreateRole;
using Nexus.Platform.Application.Roles.DeactivateRole;
using Nexus.Platform.Application.Roles.GetRole;
using Nexus.Platform.Application.Roles.GetRolePermissions;
using Nexus.Platform.Application.Roles.GetRoles;
using Nexus.Platform.Application.Roles.UpdateRole;
using Nexus.Platform.Application.Roles.UpdateRolePermissions;

namespace Nexus.Platform.Api.Roles;

/// <summary>
/// <c>/v1/permissions</c> and <c>/v1/roles</c> (domain/access.md §8; <c>contracts/openapi/platform.yaml</c>,
/// tag Roles). Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2).
/// </summary>
public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPermissionEndpoints();
        app.MapRolesEndpoints();

        return app;
    }

    private static void MapPermissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/permissions").WithTags("Roles");

        group.MapGet(string.Empty, ListPermissions)
            .WithName("listPermissions")
            .ValidatedBy<GetPermissionsQuery>()
            .RequirePermission("ROLE", PermissionAction.Read)
            .WithScope("none");
    }

    private static void MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/roles").WithTags("Roles");

        group.MapGet(string.Empty, ListRoles)
            .WithName("listRoles")
            .ValidatedBy<GetRolesQuery>()
            .RequirePermission("ROLE", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateRole)
            .WithName("createRole")
            .ValidatedBy<CreateRoleCommand>()
            .RequirePermission("ROLE", PermissionAction.Create)
            .WithScope("global");

        group.MapGet("/{id:guid}", GetRole)
            .WithName("getRole")
            .RequirePermission("ROLE", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateRole)
            .WithName("updateRole")
            .ValidatedBy<UpdateRoleCommand>()
            .RequirePermission("ROLE", PermissionAction.Update)
            .WithScope("global");

        group.MapPost("/{id:guid}/deactivate", DeactivateRole)
            .WithName("deactivateRole")
            .RequirePermission("ROLE", PermissionAction.Update)
            .WithScope("global")
            .WithRules("ACC-R9");

        group.MapGet("/{id:guid}/permissions", GetRolePermissions)
            .WithName("getRolePermissions")
            .ValidatedBy<GetRolePermissionsQuery>()
            .RequirePermission("ROLE", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}/permissions", UpdateRolePermissions)
            .WithName("updateRolePermissions")
            .ValidatedBy<UpdateRolePermissionsCommand>()
            .RequirePermission("ROLE", PermissionAction.Update)
            .WithScope("global")
            .WithRules("ACC-R7");
    }

    internal static async Task<Results<Ok<PageResponse<PermissionResponse>>, ProblemHttpResult>> ListPermissions(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetPermissionsQuery(page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<PermissionResponse>(
            value.Items.Select(PermissionResponse.From).ToList(), value.Page, value.PageSize, value.Total));
    }

    internal static async Task<Results<Ok<PageResponse<RoleListItemResponse>>, ProblemHttpResult>> ListRoles(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null,
        string? q = null,
        bool? isActive = null)
    {
        var result = await dispatcher.Send(new GetRolesQuery(page, pageSize, sort, q, isActive), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<RoleListItemResponse>(
            value.Items
                .Select(r => new RoleListItemResponse(r.Id, r.Code, r.CaptionI18n, r.IsSystem, r.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<RoleResponse>, Ok<RoleResponse>, ProblemHttpResult>> CreateRole(
        CreateRoleRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateRoleCommand(request.Id, request.Code, request.CaptionI18n, request.Description), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (role, created) = result.Value;
        response.SetETag(role.Version.ToString(CultureInfo.InvariantCulture));
        var body = RoleResponse.From(role);

        return created
            ? TypedResults.Created($"/v1/roles/{role.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> GetRole(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetRoleQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(RoleResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> UpdateRole(
        Guid id,
        UpdateRoleRequest request,
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
            new UpdateRoleCommand(id, expected, request.CaptionI18n, request.Description), cancellationToken);

        return ToResponse(result, response);
    }

    internal static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> DeactivateRole(
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

        var result = await dispatcher.Send(new DeactivateRoleCommand(id, expected), cancellationToken);
        return ToResponse(result, response);
    }

    internal static async Task<Results<Ok<PageResponse<PermissionGrantResponse>>, ProblemHttpResult>> GetRolePermissions(
        Guid id,
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        DateTimeOffset? asOf = null,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetRolePermissionsQuery(id, asOf, page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<PermissionGrantResponse>(
            value.Items.Select(PermissionGrantResponse.From).ToList(), value.Page, value.PageSize, value.Total));
    }

    internal static async Task<Results<Ok<IReadOnlyList<PermissionGrantResponse>>, ProblemHttpResult>> UpdateRolePermissions(
        Guid id,
        UpdateRolePermissionsRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new UpdateRolePermissionsCommand(id, request.PermissionCodes), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        return TypedResults.Ok<IReadOnlyList<PermissionGrantResponse>>(
            result.Value.Select(PermissionGrantResponse.From).ToList());
    }

    private static Results<Ok<RoleResponse>, ProblemHttpResult> ToResponse(Result<RoleDetails> result, HttpResponse response)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(RoleResponse.From(result.Value));
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
            problem = RoleErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
