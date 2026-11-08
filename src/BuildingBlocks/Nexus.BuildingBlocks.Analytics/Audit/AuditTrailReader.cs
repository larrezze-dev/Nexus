using System.Text.RegularExpressions;
using Nexus.BuildingBlocks.Analytics.Querying;
using Nexus.BuildingBlocks.Analytics.Scope;
using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.BuildingBlocks.Analytics.Audit;

/// <inheritdoc cref="IAuditTrailReader"/>
internal sealed partial class AuditTrailReader : IAuditTrailReader
{
    private readonly IScopeFilterProvider _scopeFilterProvider;
    private readonly IAnalyticsQueryExecutor _executor;

    public AuditTrailReader(IScopeFilterProvider scopeFilterProvider, IAnalyticsQueryExecutor executor)
    {
        _scopeFilterProvider = scopeFilterProvider;
        _executor = executor;
    }

    public async Task<PagedResult<AuditEventRecord>> ListAsync(
        string aggregateType,
        Guid aggregateId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Same rules as the contract; the endpoint validates first (400), this is the backstop.
        ArgumentNullException.ThrowIfNull(aggregateType);
        if (!AggregateTypeRegex().IsMatch(aggregateType))
        {
            throw new ArgumentException(
                $"aggregateType '{aggregateType}' does not match ^[a-z][a-z0-9]*([-_][a-z0-9]+)*$.",
                nameof(aggregateType));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, PageLimits.MaxPageSize);

        var scope = await _scopeFilterProvider.ForPermissionAsync(AuditPermissions.Read, cancellationToken).ConfigureAwait(false);
        var query = new AuditEventQuery(aggregateType, aggregateId, page, pageSize);
        return await _executor.ExecuteAsync(query, scope, cancellationToken).ConfigureAwait(false);
    }

    // Copied from the aggregateType path parameter of contracts/openapi/_shared/audit.yaml.
    [GeneratedRegex("^[a-z][a-z0-9]*([-_][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex AggregateTypeRegex();
}
