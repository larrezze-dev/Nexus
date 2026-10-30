using System.Reflection;
using FluentValidation;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Default <see cref="IDispatcher"/> implementation. Resolves the closed
/// <see cref="ICommandHandler{TCommand,TResult}"/>/<see cref="IQueryHandler{TQuery,TResult}"/>
/// for the request's runtime type via <see cref="IServiceProvider"/>, runs the matching
/// <see cref="IValidator{T}"/> (if any) before calling the handler, and reports validation
/// failures as <c>Result.Failure</c> with <see cref="Error.Code"/> <see cref="ErrorCodes.Validation"/>
/// (<c>"validation"</c>), which the web layer maps to 400.
///
/// Missing handler registrations are a programming error (a command/query with no handler
/// wired up in DI), not a business outcome, so they throw instead of returning a
/// <see cref="Result{TResult}"/> failure.
/// </summary>
internal sealed class Dispatcher : IDispatcher
{
    private static readonly ConcurrentHandleMethodCache HandleMethodCache = new();

    private readonly IServiceProvider _serviceProvider;

    public Dispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var requestType = command.GetType();
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(requestType, typeof(TResult));

        return DispatchCore<TResult>(command, requestType, handlerType, cancellationToken);
    }

    public Task<Result<TResult>> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requestType = query.GetType();
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResult));

        return DispatchCore<TResult>(query, requestType, handlerType, cancellationToken);
    }

    private async Task<Result<TResult>> DispatchCore<TResult>(
        object request,
        Type requestType,
        Type handlerType,
        CancellationToken cancellationToken)
    {
        var validationFailure = await ValidateAsync<TResult>(request, requestType, cancellationToken).ConfigureAwait(false);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var handler = _serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for '{requestType.FullName}'. " +
                $"Expected an implementation of '{handlerType.FullName}' registered via AddNexusApplication.");

        var handleMethod = HandleMethodCache.GetHandleMethod(handlerType);
        var resultTask = (Task<Result<TResult>>)handleMethod.Invoke(handler, [request, cancellationToken])!;

        return await resultTask.ConfigureAwait(false);
    }

    private async Task<Result<TResult>?> ValidateAsync<TResult>(object request, Type requestType, CancellationToken cancellationToken)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(requestType);

        if (_serviceProvider.GetService(validatorType) is not IValidator validator)
        {
            return null;
        }

        var validationContext = new ValidationContext<object>(request);
        var validationResult = await validator.ValidateAsync(validationContext, cancellationToken).ConfigureAwait(false);

        if (validationResult.IsValid)
        {
            return null;
        }

        var message = string.Join("; ", validationResult.Errors.Select(failure => $"{failure.PropertyName}: {failure.ErrorMessage}"));
        return Result.Failure<TResult>(Error.Validation(message));
    }

    /// <summary>
    /// Caches the reflected <c>Handle</c> <see cref="MethodInfo"/> per closed handler type so
    /// repeated dispatches of the same command/query do not re-resolve it via reflection.
    /// </summary>
    private sealed class ConcurrentHandleMethodCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo> _methodsByHandlerType = new();

        public MethodInfo GetHandleMethod(Type handlerType) =>
            _methodsByHandlerType.GetOrAdd(handlerType, static type =>
                type.GetMethod("Handle")
                    ?? throw new InvalidOperationException($"Handler type '{type.FullName}' does not expose a 'Handle' method."));
    }
}
