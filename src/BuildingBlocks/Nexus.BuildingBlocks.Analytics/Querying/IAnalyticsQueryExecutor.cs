using Nexus.BuildingBlocks.Analytics.Scope;

namespace Nexus.BuildingBlocks.Analytics.Querying;

/// <summary>
/// Executes analytic queries against ClickHouse under a mandatory <see cref="ScopeFilter"/>
/// (conventions-api.md §7.3). The only code path from a .NET service to a ClickHouse query.
/// </summary>
public interface IAnalyticsQueryExecutor
{
    /// <summary>
    /// Runs <paramref name="query"/> with every statement filtered by <paramref name="scope"/>.
    /// An empty filter (<see cref="ScopeFilter.IsEmpty"/>) returns
    /// <see cref="IAnalyticsQuery{TResult}.EmptyResult"/> without contacting ClickHouse.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(IAnalyticsQuery<TResult> query, ScopeFilter scope, CancellationToken cancellationToken);
}
