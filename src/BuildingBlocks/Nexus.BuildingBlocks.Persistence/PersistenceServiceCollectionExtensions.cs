using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Npgsql;

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>DI wiring of the persistence building block for a service host.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers, for a service that owns a PostgreSQL schema (conventions-backend.md §4):
    /// <list type="bullet">
    ///   <item><see cref="ServiceDataSource"/> (singleton) from <see cref="NexusPersistenceOptions"/>.
    ///   Built lazily, on first use: a host that never touches the database (e.g. the CI
    ///   conformance job, which only reads <c>/openapi/v1.json</c>) starts without one.</item>
    ///   <item><see cref="IEventSchemaProvider"/> (singleton, <see cref="FileSystemEventSchemaProvider"/>)
    ///   and <see cref="IOutboxWriter"/>.</item>
    ///   <item><see cref="UnitOfWork"/> (scoped), exposed as <see cref="IUnitOfWork"/> and as the
    ///   Npgsql-free <see cref="IRequestTransaction"/> that handlers use: same instance.</item>
    ///   <item><see cref="AuditStampingInterceptor"/> (scoped), for DbContexts that map the
    ///   standard audit columns.</item>
    /// </list>
    /// The host binds the options itself (this library has no configuration dependency):
    /// <code>
    /// builder.Services.Configure&lt;NexusPersistenceOptions&gt;(
    ///     builder.Configuration.GetSection(NexusPersistenceOptions.SectionName));
    /// builder.Services.AddNexusPersistence();
    /// </code>
    /// Requires <c>AddNexusApplication</c> (<c>IIdGenerator</c>, <c>TimeProvider</c>) and an
    /// <see cref="ICurrentUser"/> / <see cref="ICorrelationContext"/> registration (the web
    /// building block provides both for HTTP services).
    /// </summary>
    public static IServiceCollection AddNexusPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<NexusPersistenceOptions>();

        services.TryAddSingleton(sp => new ServiceDataSource(
            BuildConnectionString(sp.GetRequiredService<IOptions<NexusPersistenceOptions>>().Value)));

        services.TryAddSingleton<IEventSchemaProvider>(sp =>
        {
            var root = sp.GetRequiredService<IOptions<NexusPersistenceOptions>>().Value.EventSchemaRoot;
            return new FileSystemEventSchemaProvider(
                string.IsNullOrWhiteSpace(root) ? Path.Combine(AppContext.BaseDirectory, "contracts", "events") : root);
        });

        services.TryAddScoped<IOutboxWriter, OutboxWriter>();
        services.TryAddScoped<UnitOfWork>();
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UnitOfWork>());
        services.TryAddScoped<IRequestTransaction>(sp => sp.GetRequiredService<IUnitOfWork>());
        services.TryAddScoped<AuditStampingInterceptor>();

        return services;
    }

    private static string BuildConnectionString(NexusPersistenceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Missing configuration '{NexusPersistenceOptions.SectionName}:ConnectionString' " +
                "(the service role's Npgsql connection string, conventions-backend.md §9).");
        }

        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        if (!string.IsNullOrEmpty(options.Password))
        {
            builder.Password = options.Password;
        }

        return builder.ConnectionString;
    }
}
