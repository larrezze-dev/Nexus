using Microsoft.Extensions.Hosting;
using Nexus.OutboxRelay;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Every time-dependent piece takes TimeProvider (CLAUDE.md §3: never DateTime.UtcNow directly).
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));

// Same Nexus:Telemetry:OtlpEndpoint key as the rest of the services (architecture.md §6, port 4317 locally).
var otlpEndpoint = new Uri(builder.Configuration["Nexus:Telemetry:OtlpEndpoint"] ?? "http://localhost:4317");
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("nexus-relay"))
    .WithTracing(tracing => tracing.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint))
    .WithMetrics(metrics => metrics.AddMeter(RelayMetrics.MeterName).AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint));

builder.Services.AddOutboxRelay();

var host = builder.Build();
await host.RunAsync();
