using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Resolves and invokes the <see cref="ICommandHandler{TCommand,TResult}"/> or
/// <see cref="IQueryHandler{TQuery,TResult}"/> registered for the runtime type of the
/// command/query, running any registered FluentValidation validator first
/// (conventions-backend.md §6). No MediatR involved (ADR-0007).
/// </summary>
public interface IDispatcher
{
    Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<Result<TResult>> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
