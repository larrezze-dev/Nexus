namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Base type for the technical, HTTP-shaped exceptions this library knows how to translate into
/// a <c>application/problem+json</c> response (conventions-backend.md §7). Handlers are not
/// expected to throw these for ordinary business-rule failures - those use
/// <see cref="Nexus.BuildingBlocks.Domain.Result{T}.Failure"/>/<c>Result.Failure</c> and
/// <see cref="ResultExtensions"/> instead. These exceptions exist for the technical cases
/// (404/409/428) that no endpoint in this step raises yet, but later steps will.
/// </summary>
public abstract class NexusApiException : Exception
{
    // private protected (not protected): the parameter type is internal, so only derived types
    // within this assembly - all the sealed subclasses below - may call this constructor.
    private protected NexusApiException(NexusProblemType problemType, string detail)
        : base(detail)
    {
        ProblemType = problemType;
    }

    internal NexusProblemType ProblemType { get; }
}

/// <summary>The resource does not exist, or RLS hides it - conventions-backend.md §7 never lets
/// the caller distinguish between the two (404, code <c>NOT_FOUND</c>).</summary>
public sealed class NotFoundException : NexusApiException
{
    public NotFoundException(string detail)
        : base(NexusProblemType.NotFound, detail)
    {
    }
}

/// <summary>Optimistic-concurrency conflict (BE-R3): the `xmin`/`ETag` the caller sent no longer
/// matches the current row (409, code <c>CONCURRENCY</c>).</summary>
public sealed class ConcurrencyException : NexusApiException
{
    public ConcurrencyException(string detail)
        : base(NexusProblemType.Concurrency, detail)
    {
    }
}

/// <summary>The requested state transition is not allowed from the resource's current state
/// (409, code <c>INVALID_STATE</c>), e.g. confirming an already-cancelled job.</summary>
public sealed class InvalidStateException : NexusApiException
{
    public InvalidStateException(string currentState, string targetState)
        : base(
            NexusProblemType.InvalidState,
            $"Cannot transition from '{currentState}' to '{targetState}'.")
    {
        CurrentState = currentState;
        TargetState = targetState;
    }

    public string CurrentState { get; }

    public string TargetState { get; }
}

/// <summary>`PUT`/transition endpoints require `If-Match` (conventions-api.md §5); its absence
/// is a 428, not a 400 (code <c>PRECONDITION_REQUIRED</c>).</summary>
public sealed class PreconditionRequiredException : NexusApiException
{
    public PreconditionRequiredException(string detail)
        : base(NexusProblemType.PreconditionRequired, detail)
    {
    }
}
