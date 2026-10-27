using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nexus.BuildingBlocks.Web.Observability;

/// <summary>
/// Traces, metrics and logs, all exported over OTLP (conventions-backend.md §8,
/// architecture.md §6: port 4317 locally, Aspire Dashboard/Jaeger locally, Azure Monitor in
/// Azure). Safe to call even with no collector listening: the OTLP exporter retries in the
/// background and never throws from here or from the host's startup path.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <param name="serviceName">The OpenTelemetry resource `service.name`, e.g.
    /// <c>"asset-catalog"</c> - one per NEXUS service, set by that service's own `Program.cs`.</param>
    public static IServiceCollection AddNexusTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var options = configuration.GetSection(NexusTelemetryOptions.SectionName).Get<NexusTelemetryOptions>()
            ?? new NexusTelemetryOptions();
        var otlpEndpoint = new Uri(options.OtlpEndpoint);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint));

        services.AddLogging(logging => logging.AddOpenTelemetry(otelLogging =>
        {
            otelLogging.IncludeFormattedMessage = true;
            otelLogging.IncludeScopes = true;
            otelLogging.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint);
        }));

        return services;
    }
}
