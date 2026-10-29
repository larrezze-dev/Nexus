using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Http;
using Nexus.BuildingBlocks.Web.OpenApi;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Application.Users.CreateUser;
using Nexus.Platform.Application.Users.DeactivateUser;
using Nexus.Platform.Application.Users.GetUser;
using Nexus.Platform.Application.Users.GetUsers;
using Nexus.Platform.Application.Users.UpdateUser;

namespace Nexus.Platform.Api.Users;

/// <summary>
/// <c>/v1/users</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag Users).
/// Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2): body/route →
/// command, <c>If-Match</c> → expected version, <c>Result</c> → status + <c>ETag</c>.
/// </summary>
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/users").WithTags("Users");

        group.MapGet(string.Empty, ListUsers)
            .WithName("listUsers")
            .ValidatedBy<GetUsersQuery>()
            .RequirePermission("USER", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateUser)
            .WithName("createUser")
            .ValidatedBy<CreateUserCommand>()
            .RequirePermission("USER", PermissionAction.Create)
            .WithScope("none")
            .WithRules("ORG-R6", "ORG-R9");

        group.MapGet("/{id:guid}", GetUser)
            .WithName("getUser")
            .RequirePermission("USER", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateUser)
            .WithName("updateUser")
            .ValidatedBy<UpdateUserCommand>()
            .RequirePermission("USER", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ORG-R6", "ORG-R9");

        group.MapPost("/{id:guid}/deactivate", DeactivateUser)
            .WithName("deactivateUser")
            .RequirePermission("USER", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ORG-R6");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<UserListItemResponse>>, ProblemHttpResult>> ListUsers(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetUsersQuery(page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<UserListItemResponse>(
            value.Items
                .Select(u => new UserListItemResponse(u.Id, u.Oid, u.FullName, u.Email, u.PreferredLanguage, u.BusinessUnitId, u.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<UserResponse>, Ok<UserResponse>, ProblemHttpResult>> CreateUser(
        CreateUserRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateUserCommand(
                request.Id,
                request.Oid,
                request.FullName,
                request.Email,
                request.PreferredLanguage,
                request.BusinessUnitId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (user, created) = result.Value;
        response.SetETag(user.Version.ToString());
        var body = UserResponse.From(user);

        return created
            ? TypedResults.Created($"/v1/users/{user.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetUser(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetUserQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString());
        return TypedResults.Ok(UserResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> UpdateUser(
        Guid id,
        UpdateUserRequest request,
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
            new UpdateUserCommand(
                id,
                expected,
                request.Oid,
                request.FullName,
                request.Email,
                request.PreferredLanguage,
                request.BusinessUnitId),
            cancellationToken);

        return ToUserResponse(result, response);
    }

    internal static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> DeactivateUser(
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

        var result = await dispatcher.Send(new DeactivateUserCommand(id, expected), cancellationToken);
        return ToUserResponse(result, response);
    }

    private static Results<Ok<UserResponse>, ProblemHttpResult> ToUserResponse(
        BuildingBlocks.Domain.Result<UserDetails> result,
        HttpResponse response)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString());
        return TypedResults.Ok(UserResponse.From(result.Value));
    }

    /// <summary>
    /// <c>If-Match</c> → expected version: absent → 428; present but not a version this API
    /// ever issued → 409 (it cannot match the current one, conventions-api.md §5).
    /// </summary>
    private static bool TryReadExpectedVersion(HttpRequest request, out UserVersion expected, out ProblemHttpResult problem)
    {
        expected = default;
        problem = null!;

        if (!request.TryGetIfMatch(out var token))
        {
            problem = NexusProblems.PreconditionRequired();
            return false;
        }

        if (!UserVersion.TryParse(token, out expected))
        {
            problem = UserErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
