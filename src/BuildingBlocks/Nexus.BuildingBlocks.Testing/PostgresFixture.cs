using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Nexus.BuildingBlocks.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Nexus.BuildingBlocks.Testing;

/// <summary>
/// A real, disposable PostgreSQL 17 for any service's test project, fully bootstrapped the same
/// way the real environments are (<c>make up</c> + <c>make db-migrate</c>), so that tests do not
/// need their own stand-in roles/schema:
///
/// <list type="number">
///   <item>Starts a Postgres 17 container (Testcontainers.PostgreSql).</item>
///   <item>Runs <c>db/bootstrap/00_roles.sql</c> (the real file, unmodified) against it, as the
///   container's superuser. That script is a psql script (it reads each role's password with
///   psql's own <c>\set x `printf '%s' "$VAR"`</c>, and aborts via <c>\if</c>/<c>\warn</c> if a
///   variable is missing or empty) rather than plain SQL, so it is executed with a real
///   <c>psql</c> client -- the one already inside the official Postgres image, via
///   <see cref="IContainer.ExecAsync"/> -- instead of being sent over an ADO.NET connection
///   (Npgsql has no psql meta-command support). The 10 required passwords
///   (<c>NEXUS_MIGRATOR_PASSWORD</c>, <c>NEXUS_SVC_*_PASSWORD</c>) are generated fresh per fixture
///   instance (plain hex UUIDs: safe to splice unquoted into both the shell command and
///   connection strings) and never leave this process.</item>
///   <item>Runs Flyway (same image and CLI invocation as <c>Makefile</c>'s <c>flyway_run</c> /
///   <c>.github/workflows/ci.yml</c>'s "db" job) with <b>both</b> <c>db/migrations</c> and
///   <c>db/test-migrations</c> on <c>flyway.locations</c>, so that test-only fixtures (e.g.
///   <c>test.append_only_probe</c>) and the real schema migrate side by side in one run.
///   <c>db/migrations/afterConnect.sql</c> (a Flyway callback discovered from the
///   <c>db/migrations</c> location, but applied to every connection regardless of which location
///   triggered it) still runs <c>SET ROLE nexus_owner</c> first, so every object created here --
///   test-only or not -- is owned by <c>nexus_owner</c>, exactly like a real environment
///   (DBC-6).</item>
///   <item>Exposes a <see cref="NpgsqlDataSource"/> per <c>svc_*</c> role
///   (<see cref="GetServiceDataSource"/>) plus a generic <see cref="GetConnectionString"/> for
///   any of the 10 bootstrapped roles (including <c>nexus_migrator</c>, for tests that want to
///   run further SQL as the owner side), and <see cref="Admin"/> for superuser
///   assertions/arrangement.</item>
/// </list>
///
/// Unlike <c>Nexus.BuildingBlocks.Persistence.Tests</c>' own
/// <c>PersistenceDatabaseFixture</c> (a simpler, narrower fixture: a hand-written stand-in for
/// just the roles it needs, applying only the already-merged <c>V*__infra__*.sql</c> files
/// directly, no Flyway container, no <c>db/test-migrations</c>), this fixture runs the actual
/// bootstrap script and the actual Flyway migrate command, so that any future service's test
/// project (<c>Nexus.Catalog.Tests</c> and so on, from step 1.3 on) gets its own schema's real,
/// Flyway-versioned migrations applied for free, without re-deriving any of this wiring.
///
/// Networking: the Postgres container and the one-shot Flyway container are joined on a private
/// Testcontainers-managed Docker network (via <see cref="NetworkBuilder"/>), with the Postgres
/// container reachable on that network under the alias "postgres" -- the same hostname
/// <c>Makefile</c>'s compose network and <c>FLYWAY_URL</c> use. <c>db/</c> itself is copied into
/// the Flyway container with Testcontainers' resource-mapping ("copy a host directory into the
/// container before it starts"), not a host bind mount, so this also works when the test process
/// itself runs inside a container sharing the host's Docker socket (a bind mount's host path
/// would then resolve against the wrong filesystem; a resource mapping copies the bytes instead).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Database = "nexus";
    private const string NetworkAlias = "postgres";
    private const string FlywayImage = "flyway/flyway:13.11.1-alpine";

    /// <summary>The 10 roles <c>db/bootstrap/00_roles.sql</c> creates, and the password
    /// environment variable each one reads (see that file's own header comment).</summary>
    private static readonly IReadOnlyDictionary<string, string> RolePasswordEnvironmentVariables =
        new Dictionary<string, string>
        {
            ["nexus_migrator"] = "NEXUS_MIGRATOR_PASSWORD",
            ["svc_platform"] = "NEXUS_SVC_PLATFORM_PASSWORD",
            ["svc_catalog"] = "NEXUS_SVC_CATALOG_PASSWORD",
            ["svc_operations"] = "NEXUS_SVC_OPERATIONS_PASSWORD",
            ["svc_commercial"] = "NEXUS_SVC_COMMERCIAL_PASSWORD",
            ["svc_resources"] = "NEXUS_SVC_RESOURCES_PASSWORD",
            ["svc_controlling"] = "NEXUS_SVC_CONTROLLING_PASSWORD",
            ["svc_field"] = "NEXUS_SVC_FIELD_PASSWORD",
            ["svc_relay"] = "NEXUS_SVC_RELAY_PASSWORD",
            ["svc_solver"] = "NEXUS_SVC_SOLVER_PASSWORD",
        };

    private static readonly string[] ServiceRoles =
    [
        "svc_platform", "svc_catalog", "svc_operations", "svc_commercial",
        "svc_resources", "svc_controlling", "svc_field", "svc_relay", "svc_solver",
    ];

    // Plain lowercase hex: safe unquoted both in the `sh -c` bootstrap command and in
    // NpgsqlConnectionStringBuilder, and fresh per fixture instance/test run (never the repo's
    // own dev/CI passwords; this container is thrown away with the fixture).
    private readonly Dictionary<string, string> _rolePasswords =
        RolePasswordEnvironmentVariables.Keys.ToDictionary(role => role, _ => Guid.NewGuid().ToString("N"));

    private readonly Dictionary<string, ServiceDataSource> _serviceDataSources = new();

    private INetwork _network = null!;
    private PostgreSqlContainer _container = null!;

    /// <summary>Superuser data source, for arranging and asserting only (never for application code under test).</summary>
    public NpgsqlDataSource Admin { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        // Same exact tag as infra/compose/docker-compose.yml and the CI "db" job --
        // a pinned patch version, not a rolling "17" tag that could drift silently.
        _container = new PostgreSqlBuilder("postgres:17.11")
            .WithDatabase(Database)
            .WithNetwork(_network)
            .WithNetworkAliases(NetworkAlias)
            .Build();
        await _container.StartAsync();

        Admin = NpgsqlDataSource.Create(_container.GetConnectionString());

        await BootstrapRolesAsync();
        await RunFlywayAsync();

        foreach (var role in ServiceRoles)
        {
            _serviceDataSources[role] = new ServiceDataSource(GetConnectionString(role));
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dataSource in _serviceDataSources.Values)
        {
            await dataSource.DisposeAsync();
        }

        if (Admin is not null)
        {
            await Admin.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        // Networks cannot be deleted while a container is still attached; the Postgres
        // container above must be disposed first (the one-shot Flyway container already is,
        // from RunFlywayAsync).
        if (_network is not null)
        {
            await _network.DisposeAsync();
        }
    }

    /// <summary>Connection string authenticating as <paramref name="role"/> (any of the 10 bootstrapped roles).</summary>
    public string GetConnectionString(string role)
    {
        if (!_rolePasswords.TryGetValue(role, out var password))
        {
            throw new ArgumentException(
                $"'{role}' is not one of the roles db/bootstrap/00_roles.sql creates ({string.Join(", ", _rolePasswords.Keys)}).",
                nameof(role));
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = role,
            Password = password,
        }.ConnectionString;
    }

    /// <summary>A ready-built <see cref="ServiceDataSource"/> for one of the <c>svc_*</c> roles.</summary>
    public ServiceDataSource GetServiceDataSource(string role) =>
        _serviceDataSources.TryGetValue(role, out var dataSource)
            ? dataSource
            : throw new ArgumentException(
                $"'{role}' is not a svc_* service role ({string.Join(", ", _serviceDataSources.Keys)}).",
                nameof(role));

    /// <summary>Convenience for arrange/assert SQL as the superuser.</summary>
    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var command = Admin.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    /// <summary>
    /// Runs the real <c>db/bootstrap/00_roles.sql</c> inside the Postgres container itself (it
    /// needs a local Unix-socket, trust-authenticated <c>psql -U postgres</c>, exactly like the
    /// <c>docker-entrypoint-initdb.d</c> mechanism it documents itself as written for) with the
    /// 10 generated passwords as its expected environment variables.
    /// </summary>
    private async Task BootstrapRolesAsync()
    {
        const string ContainerPath = "/tmp/00_roles.sql";

        var scriptPath = Path.Combine(RepoRoot.Find("db/bootstrap"), "00_roles.sql");
        var scriptBytes = await File.ReadAllBytesAsync(scriptPath);
        await _container.CopyAsync(scriptBytes, ContainerPath);

        var environment = string.Join(
            ' ',
            RolePasswordEnvironmentVariables.Select(kv => $"{kv.Value}={_rolePasswords[kv.Key]}"));
        var shellCommand = $"{environment} psql -X -v ON_ERROR_STOP=1 -U postgres -d {Database} -f {ContainerPath}";

        var result = await _container.ExecAsync(["/bin/sh", "-c", shellCommand]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"db/bootstrap/00_roles.sql failed (exit {result.ExitCode}).\n--- stdout ---\n{result.Stdout}\n--- stderr ---\n{result.Stderr}");
        }
    }

    /// <summary>
    /// Runs Flyway migrate against the container, with both <c>db/migrations</c> and
    /// <c>db/test-migrations</c> on <c>-locations</c> (ficha 0.3: so test fixtures such as
    /// <c>test.append_only_probe</c> apply alongside the real schema in one run).
    /// </summary>
    private async Task RunFlywayAsync()
    {
        var dbDirectory = new DirectoryInfo(RepoRoot.Find("db"));

        const UnixFileModes DirectoryMode =
            UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute |
            UnixFileModes.GroupRead | UnixFileModes.GroupExecute |
            UnixFileModes.OtherRead | UnixFileModes.OtherExecute;

        await using var flyway = new ContainerBuilder(FlywayImage)
            .WithNetwork(_network)
            // Resource mapping (copy), not a host bind mount: works even when this test process
            // itself runs inside a container sharing the host's Docker socket, where a bind
            // mount's host path would be resolved by the wrong filesystem.
            .WithResourceMapping(dbDirectory, "/flyway/sql", 0, 0, DirectoryMode)
            .WithEnvironment("FLYWAY_PASSWORD", _rolePasswords["nexus_migrator"])
            .WithCommand(
                $"-url=jdbc:postgresql://{NetworkAlias}:5432/{Database}",
                "-user=nexus_migrator",
                "-locations=filesystem:/flyway/sql/migrations,filesystem:/flyway/sql/test-migrations",
                "migrate")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Successfully applied|is up to date|ERROR"))
            .Build();

        try
        {
            await flyway.StartAsync();
        }
        catch (ContainerNotRunningException)
        {
            // Flyway is a one-shot "job" container: it can log its final line and exit (0 = the
            // migrate succeeded) before Testcontainers' own readiness probe gets to run, which
            // this library surfaces as a (mis-named, for this case) "not running" failure rather
            // than quietly treating "already exited" as "started". The container itself still
            // exists and is still queryable (GetExitCodeAsync/GetLogsAsync below), so this is
            // swallowed and the real exit code decides success or failure, not this exception.
        }

        var exitCode = await flyway.GetExitCodeAsync();
        if (exitCode != 0)
        {
            var (stdout, stderr) = await flyway.GetLogsAsync();
            throw new InvalidOperationException($"Flyway migrate failed (exit {exitCode}).\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }
    }
}
