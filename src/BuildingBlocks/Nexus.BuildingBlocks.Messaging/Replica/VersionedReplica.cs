using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace Nexus.BuildingBlocks.Messaging.Replica;

/// <summary>
/// Npgsql implementation of <see cref="IVersionedReplica"/>. Stateless and thread-safe: the SQL is
/// built once from the validated <see cref="ReplicaTable"/> (identifiers only, always quoted) and
/// every value is a positional parameter. Register one instance per replica table (singleton).
///
/// <para>Generated statement (for <c>catalog_ref.asset_type_ref(id, aggregate_version, code, name)</c>):</para>
/// <code>
/// INSERT INTO "catalog_ref"."asset_type_ref" AS r ("id", "aggregate_version", "code", "name")
/// VALUES ($1, $2, $3, $4)
/// ON CONFLICT ("id") DO UPDATE
///    SET "aggregate_version" = EXCLUDED."aggregate_version", "code" = EXCLUDED."code", "name" = EXCLUDED."name"
///  WHERE EXCLUDED."aggregate_version" &gt; r."aggregate_version"
/// RETURNING 1
/// </code>
/// <para>A returned row means inserted or updated (<see cref="ReplicaApplyResult.Applied"/>); no
/// row means the <c>WHERE</c> rejected the update (<see cref="ReplicaApplyResult.IgnoredStale"/>).</para>
/// </summary>
public sealed class VersionedReplica : IVersionedReplica
{
    private readonly string _sql;

    public VersionedReplica(ReplicaTable table)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        _sql = BuildSql(table);
    }

    public ReplicaTable Table { get; }

    /// <summary>The generated statement (exposed for diagnostics and tests).</summary>
    public string Sql => _sql;

    public async Task<ReplicaApplyResult> ApplyAsync(
        NpgsqlTransaction transaction,
        Guid id,
        long aggregateVersion,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(aggregateVersion);

        // A completed (committed/rolled back) NpgsqlTransaction has no connection. Refusing it
        // keeps the compare-and-write from ever running in autocommit, detached from the inbox row.
        var connection = transaction.Connection
            ?? throw new InvalidOperationException(
                $"The transaction for replica '{Table}' is already completed; ApplyAsync must run inside the active unit of work.");

        ValidateValues(values);

        await using var command = new NpgsqlCommand(_sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { Value = id, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = aggregateVersion, NpgsqlDbType = NpgsqlDbType.Bigint });
        foreach (var column in Table.Columns)
        {
            var parameter = new NpgsqlParameter { Value = values[column.Name] ?? DBNull.Value };
            if (column.DbType is { } dbType)
            {
                parameter.NpgsqlDbType = dbType;
            }

            command.Parameters.Add(parameter);
        }

        var returned = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return returned is null ? ReplicaApplyResult.IgnoredStale : ReplicaApplyResult.Applied;
    }

    private void ValidateValues(IReadOnlyDictionary<string, object?> values)
    {
        // Exactly the declared columns: keys never reach the SQL text (it is fixed per table), but
        // a missing column would silently write NULL and an unknown one would silently be dropped.
        foreach (var column in Table.Columns)
        {
            if (!values.ContainsKey(column.Name))
            {
                throw new ArgumentException(
                    $"Replica '{Table}': missing value for column '{column.Name}' (master-data events carry the full state, EV-R4).",
                    nameof(values));
            }
        }

        if (values.Count != Table.Columns.Count)
        {
            var unknown = values.Keys.Where(key => Table.Columns.All(column => column.Name != key));
            throw new ArgumentException(
                $"Replica '{Table}': unknown column(s) {string.Join(", ", unknown.Select(k => $"'{k}'"))}.",
                nameof(values));
        }
    }

    private static string BuildSql(ReplicaTable table)
    {
        static string Q(string identifier) => $"\"{identifier}\"";

        var insertColumns = new List<string> { Q(table.KeyColumn), Q(table.VersionColumn) };
        insertColumns.AddRange(table.Columns.Select(column => Q(column.Name)));

        var placeholders = Enumerable.Range(1, insertColumns.Count).Select(i => $"${i}");

        var assignments = new List<string> { $"{Q(table.VersionColumn)} = EXCLUDED.{Q(table.VersionColumn)}" };
        assignments.AddRange(table.Columns.Select(column => $"{Q(column.Name)} = EXCLUDED.{Q(column.Name)}"));

        var sql = new StringBuilder()
            .Append("INSERT INTO ").Append(Q(table.Schema)).Append('.').Append(Q(table.Table)).Append(" AS r (")
            .Append(string.Join(", ", insertColumns)).Append(")\n")
            .Append("VALUES (").Append(string.Join(", ", placeholders)).Append(")\n")
            .Append("ON CONFLICT (").Append(Q(table.KeyColumn)).Append(") DO UPDATE\n")
            .Append("   SET ").Append(string.Join(", ", assignments)).Append('\n')
            .Append(" WHERE EXCLUDED.").Append(Q(table.VersionColumn)).Append(" > r.").Append(Q(table.VersionColumn)).Append('\n')
            .Append("RETURNING 1");

        return sql.ToString();
    }
}
