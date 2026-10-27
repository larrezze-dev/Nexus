using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;

namespace Nexus.BuildingBlocks.Web.Correlation;

/// <summary>
/// Establishes the correlation id of every HTTP request (conventions-backend.md §8):
/// <list type="number">
///   <item>Reads <c>X-Correlation-Id</c>. A value that is not a non-empty GUID is discarded
///   (never echoed back or logged) and replaced by a fresh UUIDv7 from
///   <see cref="IIdGenerator"/>, same as a missing header.</item>
///   <item>Stores it in the scoped <see cref="CorrelationContext"/>, from where
///   <c>UnitOfWork</c> sets <c>app.correlation_id</c> and the outbox envelope's
///   <c>correlation_id</c>.</item>
///   <item>Writes it, normalised (<c>D</c> format), to the request header (for downstream
///   propagation) and to the response header.</item>
///   <item>Opens a logging scope <c>CorrelationId</c> around the rest of the pipeline.</item>
/// </list>
/// <see cref="ICorrelationContext.CausationId"/> is left <see langword="null"/>: for an
/// HTTP-originated flow the unit of work then uses the correlation id as causation.
/// </summary>
public sealed class CorrelationMiddleware
{
    /// <summary>Name of the correlation header.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Key of the logging scope property.</summary>
    public const string LogScopeKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CorrelationContext correlation,
        IIdGenerator idGenerator,
        ILogger<CorrelationMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(correlation);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(logger);

        var correlationId = TryReadHeader(context.Request.Headers[HeaderName]) ?? idGenerator.NewId();
        var formatted = correlationId.ToString("D", CultureInfo.InvariantCulture);

        correlation.CorrelationId = correlationId;
        context.Request.Headers[HeaderName] = formatted;
        context.Response.Headers[HeaderName] = formatted;

        using (logger.BeginScope(new Dictionary<string, object> { [LogScopeKey] = formatted }))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private static Guid? TryReadHeader(Microsoft.Extensions.Primitives.StringValues values)
    {
        // Exactly one value: several X-Correlation-Id headers are ambiguous, so start a new flow.
        if (values.Count != 1)
        {
            return null;
        }

        return Guid.TryParse(values[0], CultureInfo.InvariantCulture, out var parsed) && parsed != Guid.Empty
            ? parsed
            : null;
    }
}
