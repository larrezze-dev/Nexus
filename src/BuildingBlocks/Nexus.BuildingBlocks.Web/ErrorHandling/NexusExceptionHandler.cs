using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Translates the technical exceptions this library knows about into
/// <c>application/problem+json</c> (conventions-backend.md §7):
/// <see cref="FluentValidation.ValidationException"/> → 400 with per-field <c>errors</c>;
/// a <see cref="Microsoft.AspNetCore.Http.BadHttpRequestException"/> (malformed/incomplete
/// JSON body, minimal API's own binding) → 400 <c>VALIDATION</c> too, same shape-validation
/// bucket; any <see cref="NexusApiException"/> (404/409/428) → its own status/code; an integrity
/// violation (SQLSTATE class 23) of a constraint registered in
/// <see cref="ConstraintViolationOptions"/> → that constraint's error (e.g. 422 <c>ORG-R9</c>),
/// whether it arrives bare (<see cref="PostgresException"/>) or wrapped by EF Core. Anything else is left alone (returns <see langword="false"/>)
/// so ASP.NET Core's own problem-details fallback turns it into a 500 - this handler never
/// hides a real bug behind a generic response.
/// </summary>
internal sealed class NexusExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly IOptions<ConstraintViolationOptions> _constraintViolations;

    public NexusExceptionHandler(
        IProblemDetailsService problemDetailsService,
        IOptions<ConstraintViolationOptions> constraintViolations)
    {
        _problemDetailsService = problemDetailsService;
        _constraintViolations = constraintViolations;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var mapped = Map(exception) ?? MapConstraintViolation(exception);
        if (mapped is null)
        {
            return false;
        }

        var (problemType, extensions, detail) = mapped.Value;
        httpContext.Response.StatusCode = problemType.Status;

        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = problemType.Type,
            Title = problemType.Title,
            Status = problemType.Status,
            Detail = detail,
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

    private (NexusProblemType ProblemType, Dictionary<string, object?> Extensions, string Detail)? MapConstraintViolation(Exception exception)
    {
        // EF Core wraps the server error (DbUpdateException → PostgresException); Npgsql/Dapper
        // writes throw it bare. Walk the chain instead of depending on EF Core here.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: [ '2', '3', _, _, _ ], ConstraintName: { } constraint }
                && _constraintViolations.Value.Errors.TryGetValue(constraint, out var error))
            {
                var (problemType, code) = NexusProblemType.For(error);
                return (problemType, new Dictionary<string, object?> { ["code"] = code }, error.Message);
            }
        }

        return null;
    }

    private static (NexusProblemType ProblemType, Dictionary<string, object?> Extensions, string Detail)? Map(Exception exception)
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

                return (nexusException.ProblemType, extensions, exception.Message);
            }

            // BE-R3: EF Core's xmin check failed on save — the row changed after it was loaded.
            case DbUpdateConcurrencyException:
                return (
                    NexusProblemType.Concurrency,
                    new Dictionary<string, object?> { ["code"] = NexusProblemType.Concurrency.Code },
                    "The resource was changed by someone else while this request was being processed.");

            case ValidationException validationException:
            {
                var extensions = new Dictionary<string, object?>
                {
                    ["code"] = NexusProblemType.Validation.Code,
                    ["errors"] = validationException.Errors
                        .Select(failure => new { field = failure.PropertyName, message = failure.ErrorMessage })
                        .ToArray(),
                };

                return (NexusProblemType.Validation, extensions, exception.Message);
            }

            // Minimal API's own request-body binding (missing required JSON property, malformed
            // JSON, wrong content type...) throws this before any handler/FluentValidation runs.
            // It is shape validation (conventions-backend.md §7: "validación de forma" -> 400),
            // not a technical failure - found live: a body missing CreateUserRequest.oid
            // otherwise fell through to ASP.NET Core's generic 500 fallback.
            case BadHttpRequestException badRequest:
                return (
                    NexusProblemType.Validation,
                    new Dictionary<string, object?> { ["code"] = NexusProblemType.Validation.Code },
                    badRequest.Message);

            default:
                return null;
        }
    }
}
