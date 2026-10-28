namespace Nexus.BuildingBlocks.Web.Observability;

/// <summary>Bound from the <c>Nexus:Telemetry</c> configuration section.</summary>
public sealed class NexusTelemetryOptions
{
    public const string SectionName = "Nexus:Telemetry";

    /// <summary>OTLP gRPC collector endpoint (architecture.md §6: Aspire Dashboard/Jaeger locally
    /// on this port, Azure Monitor in Azure). The exporter retries in the background and never
    /// throws from startup when nothing is listening there.</summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}
