using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Analytics.Querying;
using Nexus.BuildingBlocks.Analytics.Scope;
using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.BuildingBlocks.Analytics.Audit;

/// <inheritdoc cref="IAuditTrailReader"/>
internal sealed partial class AuditTrailReader : IAuditTrailReader
{
    private readonly IScopeFilterProvider _scopeFilterProvider;
    private readonly IAnalyticsQueryExecutor _executor;
    private readonly ILogger<AuditTrailReader> _logger;

    public AuditTrailReader(IScopeFilterProvider scopeFilterProvider, IAnalyticsQueryExecutor executor, ILogger<AuditTrailReader> logger)
    {
        _scopeFilterProvider = scopeFilterProvider;
        _executor = executor;
        _logger = logger;
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
        var query = new AuditEventQuery(aggregateType, aggregateId, page, pageSize, _logger);
        return await _executor.ExecuteAsync(query, scope, cancellationToken).ConfigureAwait(false);
    }

    // The aggregateType path parameter pattern of contracts/openapi/_shared/audit.yaml, with \z
    // instead of $: in .NET $ also matches before a trailing "\n" (ECMA's $ does not).
    [GeneratedRegex(@"^[a-z][a-z0-9]*([-_][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex AggregateTypeRegex();
}
