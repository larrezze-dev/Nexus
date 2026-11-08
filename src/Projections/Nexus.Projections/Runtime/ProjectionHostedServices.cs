using Microsoft.Extensions.Hosting;
using Nexus.Projections.Migrations;

namespace Nexus.Projections.Runtime;

/// <summary>
/// First hosted service of the worker: applies the ClickHouse migrations in <see cref="StartAsync"/>.
/// Hosted services start one after another in registration order, so no consumer starts before the
/// schema is up to date; a failure (checksum mismatch, missing file...) aborts the host start.
/// </summary>
public sealed class ClickHouseMigrationHostedService(ClickHouseMigrator migrator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => migrator.ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Runs one <see cref="ProjectionConsumer"/> per registered projection, each on its own thread-pool task
/// (librdkafka's poll blocks). If any loop dies with a fatal error the service fails at once, so the host
/// stops (<c>BackgroundServiceExceptionBehavior.StopHost</c>) and the other loops are cancelled.
/// </summary>
public sealed class ProjectionWorker : BackgroundService
{
    private readonly IReadOnlyList<ProjectionConsumer> _consumers;

    public ProjectionWorker(ProjectionCatalog catalog, ProjectionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runtime);
        // Created (and registered in /health as Starting) when the host builds the service.
        _consumers = catalog.All.Select(p => new ProjectionConsumer(p, runtime)).ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = _consumers
            .Select(c => Task.Run(() => c.RunAsync(stoppingToken), CancellationToken.None))
            .ToList();
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(done);
            await done.ConfigureAwait(false); // a faulted loop throws here and stops the host
        }
    }
}
