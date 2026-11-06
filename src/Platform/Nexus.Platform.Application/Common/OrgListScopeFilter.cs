using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Common;

/// <summary>
/// Row-scope filter of the <c>org</c> structure listings (<c>listLegalEntities</c>,
/// <c>listBusinessUnits</c>, <c>listWarehouses</c>). Their contract declares <c>x-scope: none</c>
/// (ACC-R10's coarse gate: the permission in <i>some</i> scope) and <c>org</c> has no RLS, so the
/// handler keeps only the rows whose <b>own</b> scope the caller's permission covers -- the exact
/// check the matching detail endpoint makes (<c>LEGAL_ENTITY</c>/<c>BUSINESS_UNIT</c>/
/// <c>WAREHOUSE</c> on the row's id), so list and detail always agree -- and only then paginates.
/// <para>
/// Cost: one <c>access.has_permission</c> per distinct owning legal entity, plus one per row only
/// for rows whose legal entity is <i>not</i> covered (to find narrower BU/warehouse grants). The
/// shortcut gives the same answer as the per-row check: a target that carries a legal entity is
/// matched by every GLOBAL/GROUP/LEGAL_ENTITY grant that matches that legal entity alone, and the
/// BU/warehouse targets of <see cref="IRoleAssignmentScopeChecker"/> always carry the row's own
/// legal entity (ORG-R3 keeps a warehouse's business unit inside its legal entity).
/// </para>
/// One instance per request (it caches per-legal-entity answers for one permission).
/// </summary>
internal sealed class OrgListScopeFilter
{
    private readonly IRoleAssignmentScopeChecker _scopeChecker;
    private readonly string _permission;
    private readonly Dictionary<Guid, bool> _legalEntityCoverage = new();

    public OrgListScopeFilter(IRoleAssignmentScopeChecker scopeChecker, string permission)
    {
        _scopeChecker = scopeChecker;
        _permission = permission;
    }

    /// <summary>
    /// Does the caller's permission cover the row <paramref name="scopeType"/>/
    /// <paramref name="entityId"/>, owned by <paramref name="owningLegalEntityId"/>? For a legal
    /// entity row, pass its own id as both.
    /// </summary>
    public async Task<bool> CoversAsync(
        Guid owningLegalEntityId,
        string scopeType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        if (!_legalEntityCoverage.TryGetValue(owningLegalEntityId, out var legalEntityCovered))
        {
            legalEntityCovered = await _scopeChecker.HasPermissionCoveringAsync(
                _permission, "LEGAL_ENTITY", owningLegalEntityId, cancellationToken);
            _legalEntityCoverage[owningLegalEntityId] = legalEntityCovered;
        }

        if (legalEntityCovered || scopeType == "LEGAL_ENTITY")
        {
            return legalEntityCovered;
        }

        // Legal entity not covered: only a grant on this business unit/warehouse (or, for a
        // warehouse, on its business unit) can still cover the row.
        return await _scopeChecker.HasPermissionCoveringAsync(_permission, scopeType, entityId, cancellationToken);
    }

    /// <summary>
    /// Page <paramref name="page"/> of the already scope-filtered <paramref name="visible"/>
    /// rows; <c>total</c> is what the caller can see, never the unfiltered count.
    /// </summary>
    public static PagedResult<T> Paginate<T>(IReadOnlyList<T> visible, int page, int pageSize)
    {
        var items = visible
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize)
            .ToList();

        return new PagedResult<T>(items, page, pageSize, visible.Count);
    }
}
