using System.Collections.Immutable;

namespace Nexus.BuildingBlocks.Analytics.Scope;

/// <summary>
/// The row scope of one analytic query (conventions-api.md §7, architecture.md §5.4): the legal
/// entities whose rows the caller may see, computed in PostgreSQL inside the request's
/// transaction, plus whether rows <b>without</b> a legal entity (<c>legal_entity_id IS NULL</c>)
/// are visible too.
/// <para>
/// ClickHouse has no per-user security, so this object is the only thing that stands between a
/// query and every tenant's data. It is therefore unforgeable from outside this assembly: the
/// constructor is <c>internal</c> and the only producer is <see cref="IScopeFilterProvider"/>,
/// which reads it from the <c>access</c> functions. It is a sealed class with get-only members
/// (not a record: a record's <c>with</c> expression would let a caller copy a real filter and
/// flip <see cref="IncludeUnscoped"/>), and the id list is an immutable copy.
/// </para>
/// </summary>
public sealed class ScopeFilter
{
    internal ScopeFilter(IEnumerable<Guid> legalEntityIds, bool includeUnscoped)
    {
        ArgumentNullException.ThrowIfNull(legalEntityIds);

        // Distinct + ordered: the bound array is deterministic (stable query text and logs) and
        // duplicates would only bloat the parameter.
        LegalEntityIds = legalEntityIds.Distinct().Order().ToImmutableArray();
        IncludeUnscoped = includeUnscoped;
    }

    /// <summary>Legal entities whose rows are visible. Never contains duplicates.</summary>
    public ImmutableArray<Guid> LegalEntityIds { get; }

    /// <summary>
    /// Whether rows with <c>legal_entity_id IS NULL</c> are visible as well. Only ever
    /// <see langword="true"/> for a permission held in a <c>GLOBAL</c> assignment
    /// (<see cref="IScopeFilterProvider.ForPermissionAsync"/>).
    /// </summary>
    public bool IncludeUnscoped { get; }

    /// <summary>
    /// Nothing is visible: no legal entity and no unscoped rows. The executor answers such a
    /// query with the query's empty result without contacting ClickHouse.
    /// </summary>
    public bool IsEmpty => LegalEntityIds.IsEmpty && !IncludeUnscoped;

    /// <inheritdoc />
    public override string ToString() =>
        $"ScopeFilter(legalEntities: {LegalEntityIds.Length}, includeUnscoped: {IncludeUnscoped})";
}
