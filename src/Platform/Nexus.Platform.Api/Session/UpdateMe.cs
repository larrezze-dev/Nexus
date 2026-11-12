using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.Platform.Application.Session.UpdateMe;

namespace Nexus.Platform.Api.Session;

/// <summary>
/// <c>UpdateMeRequest</c> (<c>contracts/openapi/platform.yaml</c>): <c>additionalProperties:
/// false</c>, so an unknown property is rejected by the JSON binder (400, shape validation)
/// instead of being silently ignored - a client that sends a field it believes it can change
/// here (an email, an oid) must learn that it cannot. <c>required</c>: a body without
/// <c>preferredLanguage</c> is a 400 too, and the generated document lists it as required.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateMeRequest
{
    public required string PreferredLanguage { get; init; }
}

/// <summary>Handler for <c>PATCH /v1/me</c> (<c>updateMe</c>, <c>x-permission: AUTHENTICATED</c>).</summary>
public static class UpdateMe
{
    /// <summary>
    /// Changes the caller's own <c>preferredLanguage</c> and answers with the same <c>Me</c> as
    /// <c>GET /v1/me</c> (<see cref="MeResponse.From"/>). No <c>If-Match</c> and no <c>ETag</c>
    /// (conventions-api.md §5 exception). Errors: unsupported language → 400 <c>VALIDATION</c>
    /// (conventions-backend.md §7, same as <c>PUT /v1/users/{id}</c>); SYSTEM → 422
    /// <c>ORG-R6</c>; caller deactivated meanwhile → 403 <c>user_not_provisioned</c>, like
    /// <c>GET /v1/me</c>.
    /// </summary>
    public static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> Handle(
        UpdateMeRequest request,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (currentUser.UserId is null)
        {
            // Unreachable behind IdentityResolutionMiddleware (same guard as GetMe).
            return NexusProblems.UserNotProvisioned();
        }

        var result = await dispatcher.Send(new UpdateMeCommand(request.PreferredLanguage), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.Code == ErrorCodes.NotFound
                ? NexusProblems.UserNotProvisioned()
                : result.Error.ToProblem();
        }

        return TypedResults.Ok(MeResponse.From(httpContext, currentUser, result.Value));
    }
}
