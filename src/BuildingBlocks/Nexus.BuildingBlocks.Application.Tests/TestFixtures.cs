using FluentValidation;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Application.Tests;

// Fixture types used across the dispatcher and registration tests. Kept in one file since
// they exist purely to exercise AddNexusApplication's assembly scan and IDispatcher's
// resolve-validate-handle pipeline, not as examples of real domain commands.

public sealed record GreetCommand(string Name) : ICommand<string>;

public sealed record GreetCommandHandlerCallLog
{
    public int CallCount { get; private set; }

    public void RecordCall() => CallCount++;
}

internal sealed class GreetCommandHandler : ICommandHandler<GreetCommand, string>
{
    private readonly GreetCommandHandlerCallLog _callLog;

    public GreetCommandHandler(GreetCommandHandlerCallLog callLog)
    {
        _callLog = callLog;
    }

    public Task<Result<string>> Handle(GreetCommand command, CancellationToken cancellationToken)
    {
        _callLog.RecordCall();
        return Task.FromResult(Result.Success($"Hello, {command.Name}!"));
    }
}

internal sealed class GreetCommandValidator : AbstractValidator<GreetCommand>
{
    public GreetCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
    }
}

// Command with no registered validator, to prove the no-validator path calls the handler directly.
public sealed record UnvalidatedCommand(int Value) : ICommand<int>;

internal sealed class UnvalidatedCommandHandler : ICommandHandler<UnvalidatedCommand, int>
{
    public Task<Result<int>> Handle(UnvalidatedCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(command.Value * 2));
}

public sealed record DoubleQuery(int Value) : IQuery<int>;

internal sealed class DoubleQueryHandler : IQueryHandler<DoubleQuery, int>
{
    public Task<Result<int>> Handle(DoubleQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(query.Value * 2));
}
