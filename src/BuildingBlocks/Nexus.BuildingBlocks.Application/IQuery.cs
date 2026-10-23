namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Marker for a read-only request that returns <typeparamref name="TResult"/>
/// (conventions-backend.md §6). A query is handled by exactly one
/// <see cref="IQueryHandler{TQuery,TResult}"/>, resolved by <see cref="IDispatcher"/>.
/// </summary>
/// <typeparam name="TResult">The type produced on success.</typeparam>
public interface IQuery<TResult>
{
}
