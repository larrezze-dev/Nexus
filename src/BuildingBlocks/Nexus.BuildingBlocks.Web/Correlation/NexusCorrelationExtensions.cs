using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.BuildingBlocks.Persistence;

namespace Nexus.BuildingBlocks.Web.Correlation;

/// <summary>Registration and pipeline wiring of <see cref="CorrelationMiddleware"/>.</summary>
public static class NexusCorrelationExtensions
{
    /// <summary>
    /// Registers the scoped <see cref="CorrelationContext"/> and exposes the same instance as
    /// <see cref="ICorrelationContext"/> (the middleware writes it, <c>UnitOfWork</c> reads it).
    /// Idempotent. The middleware also needs <c>IIdGenerator</c>, registered by
    /// <c>AddNexusApplication</c>.
    /// </summary>
    public static IServiceCollection AddNexusCorrelation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<CorrelationContext>();
        services.TryAddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        return services;
    }

    /// <summary>
    /// Adds <see cref="CorrelationMiddleware"/>. Place it first in the pipeline (before
    /// exception handling and authentication) so that every log line and every error response
    /// of the request carries the correlation id.
    /// </summary>
    public static IApplicationBuilder UseNexusCorrelation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<CorrelationMiddleware>();
    }
}
