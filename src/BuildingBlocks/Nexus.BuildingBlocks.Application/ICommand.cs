namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Marker for a write/state-changing request that returns <typeparamref name="TResult"/>
/// (conventions-backend.md §6). A command is handled by exactly one
/// <see cref="ICommandHandler{TCommand,TResult}"/>, resolved by <see cref="IDispatcher"/>.
/// </summary>
/// <typeparam name="TResult">The type produced on success.</typeparam>
public interface ICommand<TResult>
{
}
