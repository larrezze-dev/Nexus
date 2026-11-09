using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Testing;
using Nexus.Projections.Migrations;
using Nexus.Projections.Runtime;
using Nexus.Projections.Tests.Runtime.Infrastructure;
using Shouldly;

namespace Nexus.Projections.Tests.Runtime;

/// <summary>
/// The ClickHouse migration applier against a real ClickHouse (its own container: every test starts by
/// dropping <c>nexus_analytics</c>), following db/clickhouse/README.md.
/// </summary>
[Collection(MigratorTestGroup.Name)]
public sealed class ClickHouseMigratorTests(ClickHouseFixture clickHouse) : IAsyncLifetime
{
    private const string BaseVersion = "202611082100";
    private readonly List<string> _directories = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        await clickHouse.ExecuteAsync("DROP DATABASE IF EXISTS nexus_analytics SYNC");

    public ValueTask DisposeAsync()
    {
        foreach (var dir in _directories)
        {
            Directory.Delete(dir, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "CH migrations: a fresh ClickHouse gets the base file applied and recorded with its SHA-256")]
    public async Task Fresh_database_applies_base()
    {
        var dir = Directory_(("V202611082100__base.sql", BaseSql()));

        var applied = await Migrator(dir).ApplyAsync(Ct);

        applied.ShouldBe([BaseVersion]);
        (await clickHouse.CountAsync("SELECT count() FROM system.tables WHERE database = 'nexus_analytics' AND name IN ('event_log','dim_legal_entity','dim_warehouse','_schema_migrations')")).ShouldBe(4);
        var rows = await clickHouse.RowsAsync("SELECT version, description, checksum FROM nexus_analytics._schema_migrations");
        rows.ShouldBe([$"{BaseVersion} | base | {Sha256(BaseSql())}"]);
    }

    [Fact(DisplayName = "CH migrations: a second run is a no-op (no statement, no new history row)")]
    public async Task Rerun_is_noop()
    {
        var dir = Directory_(("V202611082100__base.sql", BaseSql()));
        await Migrator(dir).ApplyAsync(Ct);

        var applied = await Migrator(dir).ApplyAsync(Ct);

        applied.ShouldBeEmpty();
        (await clickHouse.CountAsync("SELECT count() FROM nexus_analytics._schema_migrations")).ShouldBe(1);
    }

    [Fact(DisplayName = "CH migrations: concurrent starts all succeed; duplicate history rows for a version are accepted")]
    public async Task Concurrent_appliers_succeed()
    {
        var dir = Directory_(("V202611082100__base.sql", BaseSql()), ("V202611090900__second.sql", "CREATE TABLE IF NOT EXISTS nexus_analytics.t_second (id UInt64) ENGINE = MergeTree ORDER BY id;"));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Migrator(dir).ApplyAsync(Ct), Ct)));

        results.ShouldAllBe(r => r.Count <= 2);
        var rowsPerVersion = await clickHouse.CountAsync($"SELECT count() FROM nexus_analytics._schema_migrations WHERE version = '{BaseVersion}'");
        rowsPerVersion.ShouldBeInRange(1, 4);

        // Make the duplicate certain, then start again: the checksum check reads every row and accepts equal ones.
        await clickHouse.ExecuteAsync(
            $"INSERT INTO nexus_analytics._schema_migrations VALUES ('{BaseVersion}', 'base', '{Sha256(BaseSql())}', now64(3))");
        (await Migrator(dir).ApplyAsync(Ct)).ShouldBeEmpty();
        (await clickHouse.CountAsync("SELECT count() FROM nexus_analytics._schema_migrations FINAL")).ShouldBe(2);
    }

    [Fact(DisplayName = "CH migrations: one history row with a different checksum (read without FINAL) refuses to start")]
    public async Task Tampered_checksum_refuses()
    {
        var dir = Directory_(("V202611082100__base.sql", BaseSql()));
        await Migrator(dir).ApplyAsync(Ct);
        // A later row for the same version with another checksum: FINAL alone could hide either row.
        await clickHouse.ExecuteAsync(
            $"INSERT INTO nexus_analytics._schema_migrations VALUES ('{BaseVersion}', 'base', 'deadbeef', now64(3) - INTERVAL 1 DAY)");

        var ex = await Should.ThrowAsync<ClickHouseMigrationException>(() => Migrator(dir).ApplyAsync(Ct));

        ex.Message.ShouldContain("deadbeef");
        ex.Message.ShouldContain("Refusing to start");
    }

    [Fact(DisplayName = "CH migrations: an applied file that was edited afterwards refuses to start")]
    public async Task Edited_file_refuses()
    {
        var dir = Directory_(("V202611082100__base.sql", BaseSql()));
        await Migrator(dir).ApplyAsync(Ct);
        await File.AppendAllTextAsync(Path.Combine(dir, "V202611082100__base.sql"), "\n-- edited\n", Ct);

        await Should.ThrowAsync<ClickHouseMigrationException>(() => Migrator(dir).ApplyAsync(Ct));
    }

    [Fact(DisplayName = "CH migrations: an applied version whose file is missing refuses to start")]
    public async Task Missing_file_refuses()
    {
        var full = Directory_(("V202611082100__base.sql", BaseSql()), ("V202611090900__second.sql", "CREATE TABLE IF NOT EXISTS nexus_analytics.t_second (id UInt64) ENGINE = MergeTree ORDER BY id;"));
        await Migrator(full).ApplyAsync(Ct);
        var baseOnly = Directory_(("V202611082100__base.sql", BaseSql()));

        var ex = await Should.ThrowAsync<ClickHouseMigrationException>(() => Migrator(baseOnly).ApplyAsync(Ct));

        ex.Message.ShouldContain("202611090900");
    }

    [Fact(DisplayName = "CH migrations: empty and comment-only fragments are discarded (no 'Empty query' from ClickHouse)")]
    public async Task Empty_and_comment_only_fragments_are_discarded()
    {
        const string second =
            """
            -- header comment only;
            ;
            CREATE TABLE IF NOT EXISTS nexus_analytics.t_frag (id UInt64) ENGINE = MergeTree ORDER BY id;;
               ;
            -- a trailing comment after the last statement
            ALTER TABLE nexus_analytics.t_frag ADD COLUMN IF NOT EXISTS note String;
            -- the end

            """;
        var dir = Directory_(("V202611082100__base.sql", BaseSql()), ("V202611090900__fragments.sql", second));

        var applied = await Migrator(dir).ApplyAsync(Ct);

        applied.ShouldBe([BaseVersion, "202611090900"]);
        (await clickHouse.CountAsync("SELECT count() FROM system.columns WHERE database = 'nexus_analytics' AND table = 't_frag'")).ShouldBe(2);
    }

    [Fact(DisplayName = "CH migrations: a failing statement leaves its version unrecorded, so the file runs again next start")]
    public async Task Failing_statement_is_not_recorded()
    {
        var dir = Directory_(("V202611082100__base.sql", BaseSql()), ("V202611090900__broken.sql", "CREATE TABLE nexus_analytics.t_broken (id NoSuchType) ENGINE = MergeTree ORDER BY id;"));

        await Should.ThrowAsync<ClickHouseMigrationException>(() => Migrator(dir).ApplyAsync(Ct));

        (await clickHouse.CountAsync("SELECT count() FROM nexus_analytics._schema_migrations FINAL WHERE version = '202611090900'")).ShouldBe(0);
        (await clickHouse.CountAsync($"SELECT count() FROM nexus_analytics._schema_migrations FINAL WHERE version = '{BaseVersion}'")).ShouldBe(1);
    }

    [Fact(DisplayName = "CH migrations: the shipped db/clickhouse files are copied next to the binaries (default directory)")]
    public async Task Default_directory_is_next_to_binaries()
    {
        var migrator = Migrator(directory: string.Empty);

        var applied = await migrator.ApplyAsync(Ct);

        applied.ShouldContain(BaseVersion);
    }

    private ClickHouseMigrator Migrator(string directory) =>
        new(
            new ClickHouseConnectionProvider(Options.Create(new ClickHouseOptions { ConnectionString = clickHouse.ConnectionString })),
            Options.Create(new ProjectionOptions { MigrationsDirectory = directory }),
            TimeProvider.System,
            NullLogger<ClickHouseMigrator>.Instance);

    private string Directory_(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-migrations", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _directories.Add(dir);
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(dir, name), content);
        }

        return dir;
    }

    private static string BaseSql() => File.ReadAllText(RepoRoot.Find("db/clickhouse/V202611082100__base.sql"));

    private static string Sha256(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
}

[CollectionDefinition(Name)]
public sealed class MigratorTestGroup : ICollectionFixture<ClickHouseFixture>
{
    public const string Name = "projections-migrator";
}
