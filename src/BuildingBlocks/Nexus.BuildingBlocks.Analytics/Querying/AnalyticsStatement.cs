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
/// Validated on construction (so a malformed query fails even when the filter is empty and
/// ClickHouse is never called):
/// <list type="bullet">
///   <item>the SQL contains <see cref="ScopePlaceholder"/> at least once;</item>
///   <item>the SQL contains no comment (<c>--</c>, <c>/*</c>, <c>#</c>): a placeholder inside a
///   comment (e.g. a line commented out while debugging) would pass the check above and leave
///   the query unfiltered. A placeholder inside a string literal is not detected; statements are
///   in-house constants and must keep the placeholder in the <c>WHERE</c> clause.</item>
///   <item>the placeholder appears at least as many times as the SQL names a
///   <c>nexus_analytics.</c> table (case-insensitive, quoted or not), so a <c>UNION</c> branch or
///   a subquery over another projection cannot escape the filter. One placeholder per
///   <c>FROM</c>/<c>JOIN</c> of a projection. Projections must therefore always be named
///   database-qualified (<c>nexus_analytics.event_log</c>), never relying on the connection's
///   default database.</item>
///   <item>it contains no other <c>__scope</c> token (the scope parameter is reserved for the
///   executor; a statement cannot bind or reference it by hand);</item>
///   <item>every parameter name is an identifier, is not reserved, has a non-null value, and is
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

        if (!sql.Contains(ScopePlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"An analytic statement must contain the scope placeholder '{ScopePlaceholder}' (conventions-api.md §7): " +
                "every ClickHouse query is filtered by the caller's legal entities.",
                nameof(sql));
        }

        if (sql.Contains("--", StringComparison.Ordinal)
            || sql.Contains("/*", StringComparison.Ordinal)
            || sql.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Analytic SQL must not contain comments ('--', '/*', '#'): a scope placeholder inside a comment would not filter anything.",
                nameof(sql));
        }

        var placeholders = CountOccurrences(sql, ScopePlaceholder);
        var projectionReferences = ProjectionReferenceRegex().Count(sql);
        if (placeholders < projectionReferences)
        {
            throw new ArgumentException(
                $"The statement references {projectionReferences} nexus_analytics table(s) but has only {placeholders} " +
                $"scope placeholder(s): every FROM/JOIN of a projection needs its own '{ScopePlaceholder}'.",
                nameof(sql));
        }

        if (sql.Replace(ScopePlaceholder, string.Empty, StringComparison.Ordinal).Contains(ReservedPrefix, StringComparison.Ordinal))
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

    // nexus_analytics.x, `nexus_analytics`.x, "nexus_analytics" . x
    [GeneratedRegex(@"[`""]?nexus_analytics[`""]?\s*\.", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProjectionReferenceRegex();
}
