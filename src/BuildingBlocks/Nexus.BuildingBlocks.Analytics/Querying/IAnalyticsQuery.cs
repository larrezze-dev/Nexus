namespace Nexus.BuildingBlocks.Analytics.Querying;

/// <summary>
/// An analytic (ClickHouse) query. It never sees a connection: it runs its
/// <see cref="AnalyticsStatement"/>s through the <see cref="IAnalyticsSession"/> the executor
/// hands it, which is bound to the request's <see cref="Scope.ScopeFilter"/> and injects the scope
/// predicate into every statement. A query may run several statements (e.g. a page and its
/// total); all of them are scoped.
/// </summary>
/// <typeparam name="TResult">Result type.</typeparam>
public interface IAnalyticsQuery<TResult>
{
    /// <summary>
    /// The result when the scope filter is empty (nothing visible). Returned by the executor
    /// without calling <see cref="ExecuteAsync"/> or contacting ClickHouse.
    /// </summary>
    TResult EmptyResult { get; }

    /// <summary>Runs the query's statements through <paramref name="session"/>.</summary>
    Task<TResult> ExecuteAsync(IAnalyticsSession session, CancellationToken cancellationToken);
}
