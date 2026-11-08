using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Projections.Runtime;

namespace Nexus.Projections.Migrations;

/// <summary>
/// Applies <c>db/clickhouse/V*.sql</c> to ClickHouse exactly as <c>db/clickhouse/README.md</c> prescribes.
/// Runs at startup BEFORE any consumer, and at the start of <c>rebuild</c>.
/// <list type="number">
///   <item>Connects to the <c>default</c> database (<c>nexus_analytics</c> may not exist yet).</item>
///   <item>Applied versions: <c>SELECT version FROM nexus_analytics._schema_migrations FINAL</c>, or none
///   when <c>system.tables</c> says the control table does not exist. Loaded into a SET: duplicate rows
///   for one version (concurrent starts) are expected and harmless.</item>
///   <item>Every applied version must still have its file (a missing file = refuse to start) and every
///   row of it must carry the file's checksum, read WITHOUT <c>FINAL</c> (<c>SELECT DISTINCT checksum ...</c>)
///   so a single divergent row is seen (= refuse to start).</item>
///   <item>Pending files run in version order, statement by statement (empty and comment-only fragments
///   dropped), then one history row is inserted (version, description, checksum, applied_at).</item>
/// </list>
/// No lock between instances: every statement is re-runnable, so two instances applying the same version
/// both succeed and both record it (README, "Arranques concurrentes"). Every value is a bound
/// <c>{name:Type}</c> parameter; only the migration statements themselves are sent verbatim.
/// </summary>
public sealed partial class ClickHouseMigrator
{
    internal const string Database = "nexus_analytics";
    internal const string HistoryTable = "_schema_migrations";

    private readonly ClickHouseConnectionProvider _connections;
    private readonly ProjectionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ClickHouseMigrator> _logger;

    public ClickHouseMigrator(
        ClickHouseConnectionProvider connections,
        IOptions<ProjectionOptions> options,
        TimeProvider time,
        ILogger<ClickHouseMigrator> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>Brings the schema up to date. Returns the versions this call applied.</summary>
    /// <exception cref="ClickHouseMigrationException">Checksum mismatch, missing file, malformed file, or a statement failed.</exception>
    public async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken ct)
    {
        var directory = _options.ResolveMigrationsDirectory();
        var scripts = ClickHouseMigrationScript.LoadDirectory(directory);

        using var connection = _connections.Create();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var applied = await ReadAppliedVersionsAsync(connection, ct).ConfigureAwait(false);
        var byVersion = scripts.ToDictionary(s => s.Version, StringComparer.Ordinal);

        var orphans = applied.Where(v => !byVersion.ContainsKey(v)).Order(StringComparer.Ordinal).ToList();
        if (orphans.Count > 0)
        {
            throw new ClickHouseMigrationException(
                $"ClickHouse migration(s) {string.Join(", ", orphans)} are recorded in {Database}.{HistoryTable} but their file is missing from '{directory}'. Refusing to start.");
        }

        foreach (var script in scripts.Where(s => applied.Contains(s.Version)))
        {
            await VerifyChecksumAsync(connection, script, ct).ConfigureAwait(false);
        }

        var appliedNow = new List<string>();
        foreach (var script in scripts.Where(s => !applied.Contains(s.Version)))
        {
            await ApplyScriptAsync(connection, script, ct).ConfigureAwait(false);
            appliedNow.Add(script.Version);
        }

        LogUpToDate(_logger, scripts.Count, appliedNow.Count);
        return appliedNow;
    }

    private static async Task<HashSet<string>> ReadAppliedVersionsAsync(ClickHouseConnection connection, CancellationToken ct)
    {
        var versions = new HashSet<string>(StringComparer.Ordinal);

        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT count() FROM system.tables WHERE database = {db:String} AND name = {table:String}";
            exists.AddParameter("db", Database);
            exists.AddParameter("table", HistoryTable);
            var count = Convert.ToUInt64(await exists.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (count == 0)
            {
                return versions; // first start: the base migration creates the table
            }
        }

        using var read = connection.CreateCommand();
        read.CommandText = $"SELECT version FROM {Database}.{HistoryTable} FINAL";
        using var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            versions.Add(reader.GetString(0)); // a set: concurrent starts may leave duplicate rows
        }

        return versions;
    }

    private static async Task VerifyChecksumAsync(ClickHouseConnection connection, ClickHouseMigrationScript script, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        // Without FINAL on purpose: every unmerged row of the version must match (README §5).
        command.CommandText = $"SELECT DISTINCT checksum FROM {Database}.{HistoryTable} WHERE version = {{version:String}}";
        command.AddParameter("version", script.Version);
        var recorded = new List<string>();
        using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                recorded.Add(reader.GetString(0));
            }
        }

        var mismatched = recorded.Where(c => !string.Equals(c, script.Checksum, StringComparison.Ordinal)).ToList();
        if (mismatched.Count > 0)
        {
            throw new ClickHouseMigrationException(
                $"ClickHouse migration {script} was applied with checksum {string.Join(", ", mismatched)} but the file now hashes to {script.Checksum}. " +
                "A merged migration must never be edited; add a new file instead. Refusing to start.");
        }
    }

    private async Task ApplyScriptAsync(ClickHouseConnection connection, ClickHouseMigrationScript script, CancellationToken ct)
    {
        LogApplying(_logger, script.Name, script.Statements.Count);
        for (var i = 0; i < script.Statements.Count; i++)
        {
            try
            {
                await connection.ExecuteStatementAsync(script.Statements[i]).WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The version is not recorded: the whole file (re-runnable by rule) runs again next start.
                throw new ClickHouseMigrationException(
                    $"ClickHouse migration {script} failed at statement {i + 1} of {script.Statements.Count}: {ex.Message}", ex);
            }
        }

        using var insert = connection.CreateCommand();
        insert.CommandText =
            $"INSERT INTO {Database}.{HistoryTable} (version, description, checksum, applied_at) " +
            "SELECT {version:String}, {description:String}, {checksum:String}, {applied_at:DateTime64(3, 'UTC')}";
        insert.AddParameter("version", script.Version);
        insert.AddParameter("description", script.Description);
        insert.AddParameter("checksum", script.Checksum);
        insert.AddParameter("applied_at", TruncateToMilliseconds(_time.GetUtcNow()));
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        LogApplied(_logger, script.Name);
    }

    internal static DateTime TruncateToMilliseconds(DateTimeOffset instant)
    {
        var ticks = instant.UtcTicks;
        return new DateTime(ticks - (ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying ClickHouse migration {Migration} ({Statements} statements)")]
    private static partial void LogApplying(ILogger logger, string migration, int statements);

    [LoggerMessage(Level = LogLevel.Information, Message = "ClickHouse migration {Migration} applied")]
    private static partial void LogApplied(ILogger logger, string migration);

    [LoggerMessage(Level = LogLevel.Information, Message = "ClickHouse schema up to date: {Files} migration file(s), {Applied} applied now")]
    private static partial void LogUpToDate(ILogger logger, int files, int applied);
}
