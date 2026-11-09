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
/// <b>What this validation is.</b> A heuristic guard against common mistakes in in-house,
/// compile-time constant SQL; not a parser and not a guarantee that every row read is filtered.
/// The guarantees are elsewhere: scope values are always bound (never concatenated), statements
/// are constants written in this repository (never built from request input), every connection
/// runs <c>readonly = 2</c> against the <c>default</c> database (so an unqualified projection name
/// fails with <c>UNKNOWN_TABLE</c>), and an empty scope never reaches ClickHouse.
/// </para>
/// <para>
/// <b>How it reads the SQL.</b> One left-to-right tokenizer recognises <c>'...'</c> string
/// literals and <c>`...`</c> / <c>"..."</c> quoted identifiers (doubled-quote escapes), so a quote
/// inside one token never opens another. String literals are checked for <c>nexus_analytics</c>
/// and then blanked; quoted identifiers are kept for counting projection references and spotting
/// quoted table-function names, and blanked for every other check. A placeholder inside a
/// literal or a quoted identifier therefore does not count.
/// </para>
/// <para>
/// <b>Residual limitation:</b> placement is not checked positionally. The number of placeholders
/// is compared with the number of projection references, but two placeholders in one
/// <c>UNION</c> branch and none in the other, or a placeholder in the <c>SELECT</c> list, still
/// pass. Whoever reviews a new analytic query must check by hand that every <c>FROM</c>/<c>JOIN</c>
/// of a projection (every branch, every subquery) has the placeholder in its own <c>WHERE</c>.
/// </para>
/// Rejected on construction (so a malformed query fails even when the filter is empty and
/// ClickHouse is never called):
/// <list type="bullet">
///   <item>any backslash (escape sequences could disguise a name or a literal);</item>
///   <item><c>nexus_analytics</c> inside a string literal;</item>
///   <item>no <see cref="ScopePlaceholder"/> outside literals and quoted identifiers;</item>
///   <item>comments (<c>--</c>, <c>/*</c>, <c>#</c>) outside literals and quoted identifiers;</item>
///   <item>heredoc literals (<c>$$...$$</c>, <c>$tag$...$tag$</c>), which this tokenizer does not
///   read; regex literals such as <c>'^a$'</c> are ordinary literals and stay legal;</item>
///   <item><c>{name:Identifier}</c> parameters (a bound database or table name would hide the
///   reference from the count);</item>
///   <item>table functions that read other tables or external data by name (<c>merge</c>,
///   <c>cluster</c>, <c>clusterAllReplicas</c>, <c>remote</c>, <c>remoteSecure</c>, <c>view</c>,
///   <c>loop</c>, <c>dictionary</c>, <c>url</c>, <c>file</c>, <c>s3</c>, <c>input</c>, ...), also
///   with the name quoted as an identifier;</item>
///   <item>any <c>SETTINGS</c> clause: per-query settings (database, <c>readonly</c>, time limit)
///   belong to the executor and no analytic query needs its own;</item>
///   <item>fewer placeholders than <c>nexus_analytics.</c> table references (case-insensitive,
///   quoted or not). Projections must always be named database-qualified;</item>
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

        // One left-to-right tokenizer for '...' literals, `...` and "..." quoted identifiers, so a
        // quote character inside one token can never open another. Two views:
        //  - code: literals blanked, quoted identifiers kept (so `nexus_analytics`.x and
        //    "nexus_analytics".x still count as projection references, and a quoted table
        //    function name is still seen);
        //  - bare: literals and quoted identifiers blanked, for everything else (a placeholder,
        //    comment marker, SETTINGS or heredoc inside a token is neither counted nor flagged).
        // Literals are also checked for nexus_analytics (how table functions name a database).
        var codeBuilder = new System.Text.StringBuilder(sql.Length);
        var bareBuilder = new System.Text.StringBuilder(sql.Length);
        var position = 0;
        foreach (Match token in QuotedTokenRegex().Matches(sql))
        {
            codeBuilder.Append(sql, position, token.Index - position);
            bareBuilder.Append(sql, position, token.Index - position);
            position = token.Index + token.Length;

            if (token.Value[0] == '\'')
            {
                if (token.Value.Contains("nexus_analytics", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "'nexus_analytics' must not appear inside a string literal in analytic SQL (that is how table functions name a database).",
                        nameof(sql));
                }

                codeBuilder.Append("''");
                bareBuilder.Append("''");
            }
            else
            {
                codeBuilder.Append(token.Value);
                bareBuilder.Append(token.Value[0]).Append(token.Value[0]);
            }
        }

        codeBuilder.Append(sql, position, sql.Length - position);
        bareBuilder.Append(sql, position, sql.Length - position);
        var code = codeBuilder.ToString();
        var bare = bareBuilder.ToString();

        if (!bare.Contains(ScopePlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"An analytic statement must contain the scope placeholder '{ScopePlaceholder}' outside string literals and quoted identifiers (conventions-api.md §7): " +
                "every ClickHouse query is filtered by the caller's legal entities.",
                nameof(sql));
        }

        if (bare.Contains("--", StringComparison.Ordinal)
            || bare.Contains("/*", StringComparison.Ordinal)
            || bare.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Analytic SQL must not contain comments ('--', '/*', '#'): a scope placeholder inside a comment would not filter anything.",
                nameof(sql));
        }

        if (HeredocRegex().IsMatch(bare))
        {
            throw new ArgumentException(
                "Heredoc literals ($$...$$, $tag$...$tag$) are not allowed in analytic SQL: use '...' literals.",
                nameof(sql));
        }

        if (IdentifierParameterRegex().IsMatch(bare))
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

        if (SettingsClauseRegex().IsMatch(bare))
        {
            throw new ArgumentException(
                "SETTINGS clauses are not allowed in analytic SQL: per-query settings are fixed by the executor (database, readonly, time limit).",
                nameof(sql));
        }

        var placeholders = CountOccurrences(bare, ScopePlaceholder);
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

    // '...' literal | `...` identifier | "..." identifier, each with its doubled-quote escape
    // (backslash escapes are rejected before tokenizing).
    [GeneratedRegex(@"'(?:[^']|'')*'|`(?:[^`]|``)*`|""(?:[^""]|"""")*""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedTokenRegex();

    // Start (or end) of a heredoc literal: $$ or $tag$.
    [GeneratedRegex(@"\$[A-Za-z0-9_]*\$", RegexOptions.CultureInvariant)]
    private static partial Regex HeredocRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])SETTINGS(?![A-Za-z0-9_])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SettingsClauseRegex();

    // nexus_analytics.x, `nexus_analytics`.x, "nexus_analytics" . x
    [GeneratedRegex(@"[`""]?nexus_analytics[`""]?\s*\.", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProjectionReferenceRegex();
}
