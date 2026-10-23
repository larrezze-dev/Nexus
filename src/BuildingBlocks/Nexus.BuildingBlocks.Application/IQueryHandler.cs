using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Handles a single concrete <typeparamref name="TQuery"/> and produces a
/// <see cref="Result{TResult}"/>. Registered and resolved by <see cref="IDispatcher"/>
/// via <c>AddNexusApplication</c> (conventions-backend.md §6).
/// </summary>
public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken cancellationToken);
}
