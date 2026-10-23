using Nexus.BuildingBlocks.Persistence.Outbox;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Nexus.BuildingBlocks.Persistence.Tests.PersistenceDatabaseFixture))]

namespace Nexus.BuildingBlocks.Persistence.Tests;

/// <summary>
/// Disposable PostgreSQL 17 for this project's tests. Applies the repository's real
/// <c>db/migrations/V*__infra__*.sql</c> files (read-only; infra base + outbox/inbox with their
/// exact grants), after a minimal stand-in for <c>db/bootstrap/00_roles.sql</c> (roles only).
/// Then adds a test-only schema <c>uow_test</c> with a business table and a
/// <c>number_sequence</c> table, granted to <c>svc_platform</c> the way a context migration
/// would. Services connect as <c>svc_platform</c>, which holds only INSERT on outbox.message.
///
/// Not the shared step-0.3 PostgresFixture (that one belongs to Nexus.BuildingBlocks.Testing).
/// </summary>
public sealed class PersistenceDatabaseFixture : IAsyncLifetime
{
    private const string ServicePassword = "svc_platform_test_pw";

    private static readonly string[] BusinessRoles =
        ["svc_platform", "svc_catalog", "svc_operations", "svc_commercial", "svc_resources", "svc_controlling", "svc_field"];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("nexus")
        .Build();

    /// <summary>Superuser data source, for arranging and asserting only.</summary>
    public NpgsqlDataSource Admin { get; private set; } = null!;

    /// <summary>Connection string authenticating as <c>svc_platform</c>.</summary>
    public string ServiceConnectionString { get; private set; } = null!;

    public FileSystemEventSchemaProvider SchemaProvider { get; } =
        new(Path.Combine(AppContext.BaseDirectory, "Schemas"));

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Admin = NpgsqlDataSource.Create(_container.GetConnectionString());

        var roles = string.Join('\n', BusinessRoles.Append("svc_relay").Select(r => $"CREATE ROLE {r} LOGIN PASSWORD '{ServicePassword}';"));
        await ExecuteAsync($"CREATE ROLE nexus_owner NOLOGIN;\n{roles}");

        foreach (var migration in Directory.GetFiles(FindRepoDirectory("db/migrations"), "V*__infra__*.sql").Order(StringComparer.Ordinal))
        {
            await ExecuteAsync(await File.ReadAllTextAsync(migration));
        }

        await ExecuteAsync(
            """
            CREATE SCHEMA uow_test;
            CREATE TABLE uow_test.widget (id uuid PRIMARY KEY, name text NOT NULL);
            CREATE TABLE uow_test.number_sequence (
              scope_key  text PRIMARY KEY,
              next_value bigint NOT NULL DEFAULT 1
            );
            GRANT USAGE ON SCHEMA uow_test TO svc_platform;
            GRANT SELECT, INSERT, UPDATE, DELETE ON uow_test.widget TO svc_platform;
            GRANT SELECT, INSERT, UPDATE ON uow_test.number_sequence TO svc_platform;
            """);

        ServiceConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = "svc_platform",
            Password = ServicePassword,
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (Admin is not null)
        {
            await Admin.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    public ServiceDataSource CreateServiceDataSource(int? maxPoolSize = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(ServiceConnectionString);
        if (maxPoolSize is { } size)
        {
            builder.MaxPoolSize = size;
        }

        return new ServiceDataSource(builder.ConnectionString);
    }

    public UnitOfWork CreateUnitOfWork(
        ServiceDataSource dataSource,
        Guid userId,
        Guid correlationId,
        Guid? causationId = null,
        Func<IOutboxWriter, IOutboxWriter>? decorateWriter = null)
    {
        IOutboxWriter writer = new OutboxWriter(SchemaProvider, new TestIdGenerator(), TimeProvider.System);
        if (decorateWriter is not null)
        {
            writer = decorateWriter(writer);
        }

        return new UnitOfWork(
            dataSource,
            new TestCurrentUser(userId),
            new CorrelationContext { CorrelationId = correlationId, CausationId = causationId },
            writer);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var command = Admin.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var command = Admin.CreateCommand(sql);
        foreach (var p in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = p });
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private static string FindRepoDirectory(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"'{relative}' not found above {AppContext.BaseDirectory}.");
    }
}
