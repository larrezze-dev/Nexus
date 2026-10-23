using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Handles a single concrete <typeparamref name="TCommand"/> and produces a
/// <see cref="Result{TResult}"/>. Registered and resolved by <see cref="IDispatcher"/>
/// via <c>AddNexusApplication</c> (conventions-backend.md §6).
/// </summary>
public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> Handle(TCommand command, CancellationToken cancellationToken);
}
