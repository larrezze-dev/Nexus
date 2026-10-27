using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Wires the error-handling pipeline (conventions-backend.md §7): every error response, whether
/// it comes from an unhandled exception, from ASP.NET Core's own authentication/authorization
/// middleware (401/403), from routing (404), or from a <see cref="ResultExtensions.ToMinimalApiResult"/>
/// call (422), is <c>application/problem+json</c> (RFC 9457), with the same <c>type</c>/<c>code</c>
/// pairs (<see cref="NexusProblemType"/>).
/// </summary>
public static class NexusProblemDetailsServiceCollectionExtensions
{
    /// <summary>Registers ASP.NET Core's <c>AddProblemDetails</c>, customized to stamp a stable
    /// <c>code</c> extension on every error status it did not already get from
    /// <see cref="NexusExceptionHandler"/> or <see cref="ResultExtensions"/>, plus that handler
    /// itself.</summary>
    public static IServiceCollection AddNexusProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                if (context.ProblemDetails.Extensions.ContainsKey("code"))
                {
                    return;
                }

                var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
                var problemType = status switch
                {
                    400 => NexusProblemType.BadRequest,
                    401 => NexusProblemType.Unauthorized,
                    403 => NexusProblemType.Forbidden,
                    404 => NexusProblemType.NotFound,
                    409 => NexusProblemType.Concurrency,
                    428 => NexusProblemType.PreconditionRequired,
                    _ => null,
                };

                if (problemType is null)
                {
                    return;
                }

                context.ProblemDetails.Type = problemType.Type;
                context.ProblemDetails.Title ??= problemType.Title;
                context.ProblemDetails.Extensions["code"] = problemType.Code;
            };
        });

        services.AddExceptionHandler<NexusExceptionHandler>();

        return services;
    }

    /// <summary>Turns on the pipeline registered by <see cref="AddNexusProblemDetails"/>:
    /// <c>UseExceptionHandler</c> (runs <see cref="NexusExceptionHandler"/>, falling back to
    /// ASP.NET Core's own problem-details 500 for anything unmapped) and
    /// <c>UseStatusCodePages</c> (turns an otherwise-empty 4xx/5xx response, such as the
    /// authentication/authorization middleware's bare 401/403, into a problem+json one).</summary>
    public static WebApplication UseNexusProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }
}
