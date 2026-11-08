using Dapper;
using Nexus.BuildingBlocks.Persistence;

namespace Nexus.BuildingBlocks.Analytics.Scope;

/// <inheritdoc cref="IScopeFilterProvider"/>
internal sealed class ScopeFilterProvider : IScopeFilterProvider
{
    private const string VisibleLegalEntitiesSql = "SELECT access.visible_legal_entities()";

    // One statement, one round trip. org has no RLS and every svc_* role holds SELECT on
    // org.legal_entity, so the candidate set is all legal entities (active or not: the audit
    // trail of a deactivated legal entity stays readable) and has_permission decides each one
    // with its own (GLOBAL / GROUP-of-the-LE / LEGAL_ENTITY) clauses. The second column is the
    // GLOBAL-only probe (GlobalOnlyTarget).
    private const string PermissionScopeSql =
        """
        SELECT
          coalesce(
            (SELECT array_agg(le.id ORDER BY le.id)
               FROM org.legal_entity le
              WHERE access.has_permission(@permission, le.id)),
            '{}'::uuid[]) AS legal_entity_ids,
          access.has_permission(@permission, @unreachable::uuid, @unreachable::uuid, @unreachable::uuid, @unreachable::uuid) AS include_unscoped
        """;

    private readonly IUnitOfWork _unitOfWork;

    public ScopeFilterProvider(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ScopeFilter> VisibleLegalEntitiesAsync(CancellationToken cancellationToken)
    {
        EnsureActive();

        var ids = await _unitOfWork.Connection.ExecuteScalarAsync<Guid[]>(
            new CommandDefinition(
                VisibleLegalEntitiesSql,
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken));

        return new ScopeFilter(ids ?? [], includeUnscoped: false);
    }

    public async Task<ScopeFilter> ForPermissionAsync(string permission, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        EnsureActive();

        var (legalEntityIds, includeUnscoped) = await _unitOfWork.Connection.QuerySingleAsync<(Guid[] LegalEntityIds, bool IncludeUnscoped)>(
            new CommandDefinition(
                PermissionScopeSql,
                new { permission, unreachable = GlobalOnlyTarget.NewUnreachableId() },
                _unitOfWork.Transaction,
                cancellationToken: cancellationToken));

        return new ScopeFilter(legalEntityIds ?? [], includeUnscoped);
    }

    private void EnsureActive()
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "The analytic scope must be computed inside the request's transaction (conventions-api.md §7): " +
                "the IUnitOfWork is not active. Begin it first (the permission filter does so for x-permission endpoints).");
        }
    }
}
