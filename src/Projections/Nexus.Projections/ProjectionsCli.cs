using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Projections.Projections;
using Nexus.Projections.Runtime;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nexus.Projections;

/// <summary>
/// Command line of <c>nexus-projections</c>:
/// <code>
/// nexus-projections [run] [--Key=Value ...]            the worker (migrations, then one consumer per projection, /health)
/// nexus-projections rebuild &lt;projection&gt; [--Key=Value ...]   truncate + replay one projection, then exit
/// nexus-projections help
/// </code>
/// Exit codes: <see cref="ExitOk"/>, <see cref="ExitFailure"/> (startup refused, rebuild refused or failed,
/// a consumer loop died), <see cref="ExitUsage"/> (unknown command or projection).
/// </summary>
public static partial class ProjectionsCli
{
    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;

    internal const string Usage =
        """
        Usage:
          nexus-projections [run] [--Key=Value ...]
              Applies the ClickHouse migrations, then projects Kafka events into ClickHouse
              (one consumer group projections.<name> per projection). Serves /health.
          nexus-projections rebuild <projection> [--Key=Value ...]
              Refuses if projections.<projection> has active members (stop nexus-projections first);
              otherwise deletes the group's offsets, truncates the table, replays every matching topic
              from the beginning, commits and exits.
          nexus-projections help
        """;

    /// <summary>Entry point used by Program.cs.</summary>
    public static Task<int> RunAsync(string[] args) =>
        RunAsync(args, RegisterProjections, configure: null, CancellationToken.None);

    /// <summary>
    /// THE single place where the production projections are registered (step 0.8: event_log,
    /// dim_legal_entity, dim_warehouse). A new projection is added in
    /// <see cref="ProjectionDefinitionsServiceCollectionExtensions.AddNexusProjectionDefinitions"/>.
    /// </summary>
    private static void RegisterProjections(IServiceCollection services) =>
        services.AddNexusProjectionDefinitions();

    /// <param name="registerProjections">Adds the <see cref="Abstractions.IProjection"/> singletons (tests pass their own).</param>
    /// <param name="configure">Extra configuration sources (tests).</param>
    internal static async Task<int> RunAsync(
        string[] args,
        Action<IServiceCollection> registerProjections,
        Action<IConfigurationBuilder>? configure,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(registerProjections);

        // --help / -h anywhere: usage only, never a host (a container started with --help must not consume).
        if (args.Any(a => a is "--help" or "-h" or "-?"))
        {
            await Console.Out.WriteLineAsync(Usage).ConfigureAwait(false);
            return ExitOk;
        }

        // A first argument starting with '-' is configuration (--Key=Value): the default command applies.
        var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "run";
        var rest = args.Length > 0 && !args[0].StartsWith('-') ? args[1..] : args;

        switch (command)
        {
            case "run":
                return await RunWorkerAsync(rest, registerProjections, configure, ct).ConfigureAwait(false);
            case "rebuild" when rest.Length > 0 && !rest[0].StartsWith('-'):
                return await RunRebuildAsync(rest[0], rest[1..], registerProjections, configure, ct).ConfigureAwait(false);
            case "help":
                await Console.Out.WriteLineAsync(Usage).ConfigureAwait(false);
                return ExitOk;
            default:
                await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
                return ExitUsage;
        }
    }

    private static async Task<int> RunWorkerAsync(
        string[] hostArgs, Action<IServiceCollection> registerProjections, Action<IConfigurationBuilder>? configure, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = hostArgs,
            ContentRootPath = AppContext.BaseDirectory,
        });
        configure?.Invoke(builder.Configuration);
        if (DefaultUrlsFor(builder.Configuration) is { } urls)
        {
            builder.WebHost.UseUrls(urls);
        }

        builder.Services.AddNexusProjectionsRuntime(builder.Configuration);
        registerProjections(builder.Services);
        AddTelemetry(builder.Services, builder.Configuration);

        var app = builder.Build();
        await using (app.ConfigureAwait(false))
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("nexus-projections");
            ProjectionWorker? worker = null;
            try
            {
                // Eager: instantiates every projection now (e.g. the topic registry behind event_log), so
                // a broken definition fails before any consumer or migration runs.
                var catalog = app.Services.GetRequiredService<ProjectionCatalog>();
                LogProjections(logger, catalog.Names);

                // Resolved BEFORE running: RunAsync disposes the host's services when it returns.
                worker = app.Services.GetServices<IHostedService>().OfType<ProjectionWorker>().SingleOrDefault();

                app.MapHealthChecks("/health");
                await app.RunAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogStartupFailed(logger, ex);
                return ExitFailure;
            }

            // StopHost ends RunAsync normally when a consumer loop died: report it in the exit code.
            return worker?.ExecuteTask is { IsFaulted: true } ? ExitFailure : ExitOk;
        }
    }

    private static async Task<int> RunRebuildAsync(
        string projection, string[] hostArgs, Action<IServiceCollection> registerProjections, Action<IConfigurationBuilder>? configure, CancellationToken ct)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = hostArgs,
            ContentRootPath = AppContext.BaseDirectory,
        });
        configure?.Invoke(builder.Configuration);
        builder.Services.AddNexusProjectionsRuntime(builder.Configuration);
        registerProjections(builder.Services);
        AddTelemetry(builder.Services, builder.Configuration);

        // The host is built for its DI container and logging only: it is never started, so neither the
        // migration hosted service nor the worker runs. The rebuilder applies the migrations itself.
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("nexus-projections");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cts.Cancel();
        }

        Console.CancelKeyPress += OnCancel;
        try
        {
            var catalog = host.Services.GetRequiredService<ProjectionCatalog>();
            if (catalog.Find(projection) is null)
            {
                await Console.Error.WriteLineAsync(
                    $"Unknown projection '{projection}'. Known projections: {string.Join(", ", catalog.Names)}.{Environment.NewLine}{Usage}").ConfigureAwait(false);
                return ExitUsage;
            }

            await host.Services.GetRequiredService<ProjectionRebuilder>().RunAsync(projection, cts.Token).ConfigureAwait(false);
            return ExitOk;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            LogRebuildCancelled(logger, projection);
            return ExitFailure;
        }
        catch (Exception ex)
        {
            LogRebuildFailed(logger, ex, projection);
            return ExitFailure;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }

    /// <summary>
    /// The listening address used when nothing configures one: <see cref="DefaultUrls"/> (loopback, local
    /// dev). Returns null as soon as any standard source sets the addresses — <c>ASPNETCORE_URLS</c> /
    /// <c>--urls</c> / <c>Urls</c> in configuration (key <c>urls</c>), or <c>ASPNETCORE_HTTP_PORTS</c> /
    /// <c>ASPNETCORE_HTTPS_PORTS</c> (keys <c>http_ports</c> / <c>https_ports</c>, set by the .NET container
    /// images) — so those always win. Deliberately NOT in appsettings.json: an <c>Urls</c> key there
    /// would override <c>ASPNETCORE_URLS</c> (appsettings is read after the ASPNETCORE_ environment variables).
    /// </summary>
    internal static string? DefaultUrlsFor(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string[] keys = ["urls", "http_ports", "https_ports"];
        return keys.Any(k => !string.IsNullOrWhiteSpace(configuration[k])) ? null : DefaultUrls;
    }

    /// <summary>Default <c>/health</c> address of a local run (port 5301; compose sets <c>ASPNETCORE_URLS=http://+:5301</c>).</summary>
    internal const string DefaultUrls = "http://localhost:5301";

    /// <summary>OTLP traces and metrics, same key as every service (<c>Nexus:Telemetry:OtlpEndpoint</c>, port 4317 locally).</summary>
    private static void AddTelemetry(IServiceCollection services, ConfigurationManager configuration)
    {
        var otlpEndpoint = new Uri(configuration["Nexus:Telemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("nexus-projections"))
            .WithTracing(tracing => tracing.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint))
            .WithMetrics(metrics => metrics.AddMeter(ProjectionMetrics.MeterName).AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "nexus-projections: registered projections {Projections}")]
    private static partial void LogProjections(ILogger logger, IReadOnlyList<string> projections);

    [LoggerMessage(Level = LogLevel.Critical, Message = "nexus-projections could not start")]
    private static partial void LogStartupFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rebuild of {Projection} failed")]
    private static partial void LogRebuildFailed(ILogger logger, Exception ex, string projection);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rebuild of {Projection} cancelled; nothing more was committed, run it again")]
    private static partial void LogRebuildCancelled(ILogger logger, string projection);
}
