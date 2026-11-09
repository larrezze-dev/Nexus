using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.BuildingBlocks.Analytics.Audit;

/// <summary>Permission codes of the audit trail.</summary>
public static class AuditPermissions
{
    /// <summary><c>AUDIT:READ</c>: the endpoint's <c>x-permission</c> and the scope of its rows.</summary>
    public const string Read = "AUDIT:READ";
}

/// <summary>
/// Reads the audit trail of one aggregate from <c>nexus_analytics.event_log</c>
/// (conventions-api.md §7 <c>[+]</c>, <c>contracts/openapi/_shared/audit.yaml</c>). The only reader
/// of <c>event_log</c>. Scoped (one per request): it computes its own scope with
/// <see cref="Scope.IScopeFilterProvider.ForPermissionAsync"/>(<see cref="AuditPermissions.Read"/>)
/// on the request's active unit of work.
/// </summary>
public interface IAuditTrailReader
{
    /// <summary>
    /// One page of the events of <paramref name="aggregateType"/>/<paramref name="aggregateId"/>,
    /// <c>aggregateVersion</c> descending, restricted to rows whose <c>legal_entity_id</c> is a
    /// legal entity where the caller holds <c>AUDIT:READ</c> (plus rows without a legal entity
    /// when the caller holds it in a <c>GLOBAL</c> assignment). An unknown or invisible aggregate
    /// yields an empty page with <c>total = 0</c>. <c>Total</c> counts the visible events.
    /// </summary>
    /// <param name="aggregateType">Literal <c>nexus-aggregate-type</c> header value; must match
    /// <c>^[a-z][a-z0-9]*([-_][a-z0-9]+)*$</c> (the contract's pattern).</param>
    /// <param name="aggregateId">Aggregate id.</param>
    /// <param name="page">Page number, from 1.</param>
    /// <param name="pageSize">Page size, 1..<see cref="PageLimits.MaxPageSize"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Invalid <paramref name="aggregateType"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Invalid <paramref name="page"/> or <paramref name="pageSize"/>.</exception>
    /// <exception cref="InvalidOperationException">The request's unit of work is not active.</exception>
    Task<PagedResult<AuditEventRecord>> ListAsync(
        string aggregateType,
        Guid aggregateId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
