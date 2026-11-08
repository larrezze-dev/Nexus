using System.Data;

namespace Nexus.BuildingBlocks.Analytics.Querying;

/// <summary>
/// The ClickHouse access an <see cref="IAnalyticsQuery{TResult}"/> gets for the duration of one
/// execution: scoped to one <see cref="Scope.ScopeFilter"/>, valid only inside
/// <see cref="IAnalyticsQuery{TResult}.ExecuteAsync"/>. Statements run sequentially.
/// </summary>
public interface IAnalyticsSession
{
    /// <summary>
    /// Runs <paramref name="statement"/> with the scope predicate injected and maps every row
    /// with <paramref name="map"/> (called once per row, the record is only valid during the call).
    /// Column values are the ClickHouse client's CLR types (<see cref="Guid"/> for UUID,
    /// <see cref="ulong"/> for UInt64, <see cref="string"/>, <see cref="DateTime"/> for
    /// DateTime64, <see cref="DBNull"/> for a NULL).
    /// </summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(AnalyticsStatement statement, Func<IDataRecord, T> map, CancellationToken cancellationToken);
}
