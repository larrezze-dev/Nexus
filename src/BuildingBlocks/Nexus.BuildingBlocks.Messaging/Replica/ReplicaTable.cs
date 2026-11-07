using System.Text.RegularExpressions;
using NpgsqlTypes;

namespace Nexus.BuildingBlocks.Messaging.Replica;

/// <summary>
/// One state column of a <c>*_ref</c> replica table. <paramref name="DbType"/> is optional and
/// only needed when PostgreSQL cannot infer the target type from the CLR value (e.g. a
/// <c>string</c> going into a <c>jsonb</c> column: pass <see cref="NpgsqlDbType.Jsonb"/>).
/// </summary>
public sealed record ReplicaColumn(string Name, NpgsqlDbType? DbType = null)
{
    public static implicit operator ReplicaColumn(string name) => new(name);
}

/// <summary>
/// Describes a local <c>*_ref</c> replica table fed by master-data events (EV-R4: they carry the
/// full aggregate state, so every apply writes every declared column).
///
/// <para><b>Table contract</b> (the replica's own migration, written by the owning service's
/// track C, must provide it):</para>
/// <list type="bullet">
///   <item>A key column (default <c>id</c>, <c>uuid</c>) with a <c>PRIMARY KEY</c> or
///   <c>UNIQUE</c> constraint: it is the <c>ON CONFLICT</c> arbiter and equals the event's
///   <c>aggregate_id</c>.</item>
///   <item><c>aggregate_version bigint NOT NULL</c> (name configurable): the
///   <c>nexus-aggregate-version</c> of the last applied event.</item>
///   <item>The state columns listed in <see cref="Columns"/>.</item>
/// </list>
///
/// <para><b>Identifiers are never taken from untrusted input.</b> Every schema, table and column
/// name is validated on construction against <see cref="IdentifierPattern"/> (lowercase
/// snake_case, 1–63 chars, PostgreSQL's <c>NAMEDATALEN - 1</c>) and is additionally emitted
/// double-quoted in the generated SQL. Values are always bound as parameters.</para>
/// </summary>
public sealed class ReplicaTable
{
    /// <summary>
    /// Allowed identifier shape. Anchored with <c>\z</c>, not <c>$</c>: in .NET <c>$</c> also
    /// matches before a trailing <c>\n</c>, which would let <c>"name\n"</c> through.
    /// </summary>
    public const string IdentifierPattern = @"^[a-z_][a-z0-9_]{0,62}\z";

    private static readonly Regex IdentifierRegex = new(IdentifierPattern, RegexOptions.CultureInvariant);

    public ReplicaTable(
        string schema,
        string table,
        IReadOnlyList<ReplicaColumn> columns,
        string keyColumn = "id",
        string versionColumn = "aggregate_version")
    {
        ArgumentNullException.ThrowIfNull(columns);

        Schema = ValidateIdentifier(schema, nameof(schema));
        Table = ValidateIdentifier(table, nameof(table));
        KeyColumn = ValidateIdentifier(keyColumn, nameof(keyColumn));
        VersionColumn = ValidateIdentifier(versionColumn, nameof(versionColumn));

        if (KeyColumn == VersionColumn)
        {
            throw new ArgumentException("The key column and the version column must differ.", nameof(versionColumn));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { KeyColumn, VersionColumn };
        foreach (var column in columns)
        {
            ArgumentNullException.ThrowIfNull(column, nameof(columns));
            ValidateIdentifier(column.Name, nameof(columns));
            if (!seen.Add(column.Name))
            {
                throw new ArgumentException(
                    $"Column '{column.Name}' is declared twice or collides with the key/version column.",
                    nameof(columns));
            }
        }

        Columns = columns.ToArray();
    }

    public string Schema { get; }

    public string Table { get; }

    public string KeyColumn { get; }

    public string VersionColumn { get; }

    /// <summary>State columns (excluding key and version), in declaration order.</summary>
    public IReadOnlyList<ReplicaColumn> Columns { get; }

    public override string ToString() => $"{Schema}.{Table}";

    private static string ValidateIdentifier(string? value, string parameterName)
    {
        if (value is null || !IdentifierRegex.IsMatch(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid identifier (must match {IdentifierPattern}).",
                parameterName);
        }

        return value;
    }
}
