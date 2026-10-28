using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Translates the technical exceptions this library knows about into
/// <c>application/problem+json</c> (conventions-backend.md §7):
/// <see cref="FluentValidation.ValidationException"/> → 400 with per-field <c>errors</c>;
/// any <see cref="NexusApiException"/> (404/409/428, not yet raised by any endpoint in this
/// step) → its own status/code. Anything else is left alone (returns <see langword="false"/>)
/// so ASP.NET Core's own problem-details fallback turns it into a 500 - this handler never
/// hides a real bug behind a generic response.
/// </summary>
internal sealed class NexusExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public NexusExceptionHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var mapped = Map(exception);
        if (mapped is null)
        {
            return false;
        }

        var (problemType, extensions) = mapped.Value;
        httpContext.Response.StatusCode = problemType.Status;

        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = problemType.Type,
            Title = problemType.Title,
            Status = problemType.Status,
            Detail = exception.Message,
        };
        foreach (var (key, value) in extensions)
        {
            problemDetails.Extensions[key] = value;
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        });
    }

    private static (NexusProblemType ProblemType, Dictionary<string, object?> Extensions)? Map(Exception exception)
    {
        switch (exception)
        {
            case NexusApiException nexusException:
            {
                var extensions = new Dictionary<string, object?> { ["code"] = nexusException.ProblemType.Code };
                if (nexusException is InvalidStateException invalidState)
                {
                    extensions["currentState"] = invalidState.CurrentState;
                    extensions["targetState"] = invalidState.TargetState;
                }

                return (nexusException.ProblemType, extensions);
            }

            case ValidationException validationException:
            {
                var extensions = new Dictionary<string, object?>
                {
                    ["code"] = NexusProblemType.Validation.Code,
                    ["errors"] = validationException.Errors
                        .Select(failure => new { field = failure.PropertyName, message = failure.ErrorMessage })
                        .ToArray(),
                };

                return (NexusProblemType.Validation, extensions);
            }

            default:
                return null;
        }
    }
}
