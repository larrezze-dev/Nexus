using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Nexus.BuildingBlocks.Analytics.Querying;

/// <summary>
/// One ClickHouse statement of an analytic query: SQL with ClickHouse query parameters
/// (<c>{name:Type}</c>, bound server-side, never concatenated) and the mandatory scope
/// placeholder <see cref="ScopePlaceholder"/>.
/// <para>
/// The executor replaces every occurrence of <see cref="ScopePlaceholder"/> with the scope
/// predicate of the request's <see cref="Scope.ScopeFilter"/>:
/// <c>(legal_entity_id IN {__scope_le:Array(UUID)})</c>, or
/// <c>(legal_entity_id IN {__scope_le:Array(UUID)} OR legal_entity_id IS NULL)</c> when the filter
/// includes unscoped rows. The id list is a bound <c>Array(UUID)</c> parameter. Put the
/// placeholder in the <c>WHERE</c> clause of every <c>FROM</c> that reads a projection, combined
/// with <c>AND</c> (the predicate is parenthesized). The predicate references the column
/// <c>legal_entity_id</c> unqualified, which every projection has (conventions-events.md §8).
/// </para>
/// <para>
/// <b>What the validation is, and is not.</b> The checks below are a heuristic guard against
/// common mistakes in in-house constant SQL; they are <b>not</b> a parser and <b>not</b> a
/// guarantee that every row read is filtered. The real guarantees are elsewhere: the scope values
/// are always bound (never concatenated), statements are compile-time constants written in this
/// repository (never built from request input), the connection runs <c>readonly = 2</c> against
/// the <c>default</c> database (so an unqualified projection name fails with
/// <c>UNKNOWN_TABLE</c>), and an empty scope never reaches ClickHouse.
/// </para>
/// <para>
/// <b>Remaining limitation:</b> placement is not checked positionally. The placeholder count is
/// compared with the number of projection references, but a statement with two placeholders in
/// one <c>UNION</c> branch and none in the other, or a placeholder in the <c>SELECT</c> list, still
/// passes. Whoever reviews a new analytic query must check by hand that every <c>FROM</c>/<c>JOIN</c>
/// of a projection (every branch, every subquery) has the placeholder in its own <c>WHERE</c>.
/// </para>
/// Rejected on construction (so a malformed query fails even when the filter is empty and
/// ClickHouse is never called):
/// String literals are tokenized first: <c>nexus_analytics</c> is looked for only inside them,
/// and every other check runs on the SQL with literals blanked (so a literal neither triggers a
/// false positive nor hides or fakes a placeholder).
/// <list type="bullet">
///   <item>any backslash (escape sequences could disguise a name or a literal);</item>
///   <item><c>nexus_analytics</c> inside a string literal (how table functions name a database);</item>
///   <item>no <see cref="ScopePlaceholder"/> outside string literals;</item>
///   <item>comments (<c>--</c>, <c>/*</c>, <c>#</c>): a placeholder in a commented-out line would
///   count without filtering anything;</item>
///   <item>fewer placeholders than <c>nexus_analytics.</c> table references (case-insensitive,
///   quoted or not). Projections must always be named database-qualified;</item>
///   <item><c>{name:Identifier}</c> parameters (a bound database or table name would hide the
///   reference from the count above);</item>
///   <item>table functions that read other tables or external data by name (<c>merge</c>,
///   <c>cluster</c>, <c>clusterAllReplicas</c>, <c>remote</c>, <c>remoteSecure</c>, <c>view</c>,
///   <c>loop</c>, <c>dictionary</c>, <c>url</c>, <c>file</c>, <c>s3</c>, <c>input</c>, ...), also
///   with the name quoted as an identifier;</item>
///   <item>any <c>SETTINGS</c> clause: per-query settings (database, <c>readonly</c>, time limit)
///   belong to the executor, and no analytic query needs its own. A column named
///   <c>settings</c> would be rejected too; projections have none;</item>
///   <item>any other <c>__scope</c> token (the scope parameter is reserved for the executor);</item>
///   <item>a parameter name that is not an identifier, is reserved, has a null value, or is not
///   referenced in the SQL as <c>{name:Type}</c>.</item>
/// </list>
/// Immutable.
/// </summary>
public sealed partial class AnalyticsStatement
{
    /// <summary>The placeholder the executor replaces with the scope predicate.</summary>
    public const string ScopePlaceholder = "{__scope_predicate}";

    /// <summary>Prefix reserved for the executor's own parameters and placeholder.</summary>
    internal const string ReservedPrefix = "__scope";

    /// <summary>The executor's bound parameter holding <see cref="Scope.ScopeFilter.LegalEntityIds"/>.</summary>
    internal const string ScopeLegalEntitiesParameter = "__scope_le";

    /// <summary>Creates a statement. See the type remarks for the validation rules.</summary>
    /// <param name="sql">ClickHouse SQL containing <see cref="ScopePlaceholder"/>.</param>
    /// <param name="parameters">Named query parameters (<c>{name:Type}</c> in <paramref name="sql"/>).</param>
    public AnalyticsStatement(string sql, IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        // Escapes (\x6E, \', ...) would let a name or a literal hide what it says from every
        // check below; analytic SQL never needs them.
        if (sql.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("Analytic SQL must not contain a backslash (escape sequences).", nameof(sql));
        }

        // Tokenize string literals: 'nexus_analytics' is only looked for inside them, and every
        // other check runs on the SQL with literals blanked, so a literal can neither trigger a
        // false positive ('a--b', 'file(x)', 'settings.updated') nor hide or fake a placeholder.
        foreach (Match literal in StringLiteralRegex().Matches(sql))
        {
            if (literal.Value.Contains("nexus_analytics", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "'nexus_analytics' must not appear inside a string literal in analytic SQL (that is how table functions name a database).",
                    nameof(sql));
            }
        }

        var code = StringLiteralRegex().Replace(sql, "''");

        if (!code.Contains(ScopePlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"An analytic statement must contain the scope placeholder '{ScopePlaceholder}' outside string literals (conventions-api.md §7): " +
                "every ClickHouse query is filtered by the caller's legal entities.",
                nameof(sql));
        }

        if (code.Contains("--", StringComparison.Ordinal)
            || code.Contains("/*", StringComparison.Ordinal)
            || code.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Analytic SQL must not contain comments ('--', '/*', '#'): a scope placeholder inside a comment would not filter anything.",
                nameof(sql));
        }

        if (IdentifierParameterRegex().IsMatch(code))
        {
            throw new ArgumentException(
                "Identifier parameters ({name:Identifier}) are not allowed in analytic SQL: name projections literally.",
                nameof(sql));
        }

        if (ForbiddenTableFunctionRegex().Match(code) is { Success: true } tableFunction)
        {
            throw new ArgumentException(
                $"Table function '{tableFunction.Groups["fn"].Value}' is not allowed in analytic SQL: it can read a projection without the scope filter.",
                nameof(sql));
        }

        if (SettingsClauseRegex().IsMatch(code))
        {
            throw new ArgumentException(
                "SETTINGS clauses are not allowed in analytic SQL: per-query settings are fixed by the executor (database, readonly, time limit).",
                nameof(sql));
        }

        var placeholders = CountOccurrences(code, ScopePlaceholder);
        var projectionReferences = ProjectionReferenceRegex().Count(code);
        if (placeholders < projectionReferences)
        {
            throw new ArgumentException(
                $"The statement references {projectionReferences} nexus_analytics table(s) but has only {placeholders} " +
                $"scope placeholder(s): every FROM/JOIN of a projection needs its own '{ScopePlaceholder}'.",
                nameof(sql));
        }

        if (code.Replace(ScopePlaceholder, string.Empty, StringComparison.Ordinal).Contains(ReservedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{ReservedPrefix}' is reserved for the executor's scope filter; use only the placeholder '{ScopePlaceholder}'.",
                nameof(sql));
        }

        var bound = ImmutableDictionary.CreateBuilder<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters ?? ImmutableDictionary<string, object>.Empty)
        {
            if (!IdentifierRegex().IsMatch(name))
            {
                throw new ArgumentException($"Invalid analytic parameter name '{name}'.", nameof(parameters));
            }

            if (name.StartsWith(ReservedPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Parameter name '{name}' uses the reserved prefix '{ReservedPrefix}'.", nameof(parameters));
            }

            if (value is null)
            {
                throw new ArgumentException($"Parameter '{name}' has a null value.", nameof(parameters));
            }

            if (!sql.Contains("{" + name + ":", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Parameter '{name}' is not referenced in the SQL as {{{name}:Type}}.", nameof(parameters));
            }

            bound[name] = value;
        }

        Sql = sql;
        Parameters = bound.ToImmutable();
    }

    /// <summary>The SQL as declared, with the placeholder still in it.</summary>
    public string Sql { get; }

    /// <summary>The statement's own parameters (never the scope parameter).</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // \z, not $: $ also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@":\s*Identifier\s*\}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IdentifierParameterRegex();

    // Table functions that read another table, another server or external data by name; the
    // name may be quoted as an identifier (`merge`(, "merge"(). The lookbehind keeps aggregate
    // combinators such as sumMerge( out.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_])[`""]?(?<fn>merge|cluster|clusterAllReplicas|remote|remoteSecure|view|loop|dictionary|url|file|s3|s3Cluster|input|" +
        @"mysql|postgresql|jdbc|odbc|hdfs|azureBlobStorage|gcs|executable|mergeTreeIndex|mergeTreeProjection)[`""]?\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenTableFunctionRegex();

    // A ClickHouse string literal: '...' with '' or \x escapes (backslashes are rejected before
    // tokenizing, the alternative is kept so the tokenizer is right on its own).
    [GeneratedRegex(@"'(?:[^'\\]|\\.|'')*'", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])SETTINGS(?![A-Za-z0-9_])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SettingsClauseRegex();

    // nexus_analytics.x, `nexus_analytics`.x, "nexus_analytics" . x
    [GeneratedRegex(@"[`""]?nexus_analytics[`""]?\s*\.", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProjectionReferenceRegex();
}
