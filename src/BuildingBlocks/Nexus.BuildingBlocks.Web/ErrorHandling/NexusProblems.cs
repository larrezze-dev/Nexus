using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Problem responses that endpoints produce directly (not from a failed <c>Result</c>), with the
/// same <c>type</c>/<c>code</c> pairs as the rest of the pipeline (<see cref="NexusProblemType"/>).
/// Returned, not thrown: an expected 4xx is not an exception and must not be logged as one.
/// </summary>
public static class NexusProblems
{
    /// <summary>428 <c>PRECONDITION_REQUIRED</c>: a <c>PUT</c>/transition without <c>If-Match</c> (conventions-api.md §5).</summary>
    public static ProblemHttpResult PreconditionRequired() => Create(
        NexusProblemType.PreconditionRequired,
        "This operation requires an If-Match header with the resource's current ETag.");

    /// <summary>403 <c>user_not_provisioned</c>: the token's <c>oid</c> has no active <c>app_user</c> (ADR-0006).</summary>
    public static ProblemHttpResult UserNotProvisioned() => Create(
        NexusProblemType.UserNotProvisioned,
        "No active app_user exists for this token.");

    internal static ProblemHttpResult Create(NexusProblemType problemType, string detail, string? code = null) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: problemType.Status,
            title: problemType.Title,
            type: problemType.Type,
            extensions: new Dictionary<string, object?> { ["code"] = code ?? problemType.Code });
}
