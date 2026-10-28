using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Minimal-API endpoint handlers return a <see cref="Result"/>/<see cref="Result{T}"/>
/// (conventions-backend.md §3); these extensions turn that into the <see cref="IResult"/> the
/// Minimal API pipeline needs: the success branch is up to the caller (there is no one shape for
/// every 200/201/204), the failure branch is always the same 422 <c>application/problem+json</c>
/// with <c>code</c> set to <see cref="Error.Code"/> (conventions-backend.md §7).
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
            : ToBusinessRuleProblem(result.Error!);
    }

    /// <summary>Maps a <see cref="Result{T}"/> to an <see cref="IResult"/>: <paramref name="onSuccess"/>
    /// receives the value on success (defaults to <c>200 OK</c> with the value as body), a 422
    /// problem on failure.</summary>
    public static IResult ToMinimalApiResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        Guard.Against.Null(result, nameof(result));

        return result.IsSuccess
            ? onSuccess is null ? Results.Ok(result.Value) : onSuccess(result.Value)
            : ToBusinessRuleProblem(result.Error!);
    }

    private static IResult ToBusinessRuleProblem(Error error)
    {
        var problemType = NexusProblemType.BusinessRule;

        return Results.Problem(
            detail: error.Message,
            statusCode: problemType.Status,
            title: problemType.Title,
            type: problemType.Type,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
