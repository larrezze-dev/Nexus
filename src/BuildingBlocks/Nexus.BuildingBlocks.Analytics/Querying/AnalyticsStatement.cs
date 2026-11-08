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

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
