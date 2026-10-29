using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Minimal-API endpoint handlers return a <see cref="Result"/>/<see cref="Result{T}"/>
/// (conventions-backend.md §3); these extensions turn that into the <see cref="IResult"/> the
/// Minimal API pipeline needs: the success branch is up to the caller (there is no one shape for
/// every 200/201/204); the failure branch is an <c>application/problem+json</c> whose status
/// follows conventions-backend.md §7:
/// <list type="bullet">
///   <item>A business-rule id (<c>"ORG-R9"</c>) → 422 <c>business_rule</c>, <c>code</c> = the id.</item>
///   <item>The well-known <see cref="ErrorCodes"/> → their own status, with the stable
///   upper-case <c>code</c> of <see cref="NexusProblemType"/>:
///   <see cref="ErrorCodes.Validation"/> → 400 <c>VALIDATION</c>,
///   <see cref="ErrorCodes.NotFound"/> → 404 <c>NOT_FOUND</c>,
///   <see cref="ErrorCodes.Concurrency"/> → 409 <c>CONCURRENCY</c>,
///   <see cref="ErrorCodes.Conflict"/> → 409 <c>CONFLICT</c>.</item>
/// </list>
/// </summary>
public static class ResultExtensions
{
    /// <summary>Maps a <see cref="Result"/> to an <see cref="IResult"/>: <paramref name="onSuccess"/>
    /// on success (defaults to <c>200 OK</c>), a 422 problem on failure.</summary>
    public static IResult ToMinimalApiResult(this Result result, Func<IResult>? onSuccess = null)
    {
        Guard.Against.Null(result, nameof(result));

        return result.IsSuccess
            ? onSuccess is null ? Results.Ok() : onSuccess()
            : ToProblem(result.Error!);
    }

    /// <summary>Maps a <see cref="Result{T}"/> to an <see cref="IResult"/>: <paramref name="onSuccess"/>
    /// receives the value on success (defaults to <c>200 OK</c> with the value as body), a 422
    /// problem on failure.</summary>
    public static IResult ToMinimalApiResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        Guard.Against.Null(result, nameof(result));

        return result.IsSuccess
            ? onSuccess is null ? Results.Ok(result.Value) : onSuccess(result.Value)
            : ToProblem(result.Error!);
    }

    /// <summary>The <c>application/problem+json</c> result for a failed <see cref="Result"/>.</summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        Guard.Against.Null(error, nameof(error));

        var (problemType, code) = NexusProblemType.For(error);
        return NexusProblems.Create(problemType, error.Message, code);
    }
}
