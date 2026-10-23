using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Application.Tests;

/// <summary>
/// Exercises <see cref="IDispatcher"/> end to end through DI (as registered by
/// <see cref="ServiceCollectionExtensions.AddNexusApplication"/>), rather than constructing the
/// internal <c>Dispatcher</c> directly - the dispatcher is only meant to be consumed through
/// <see cref="IDispatcher"/> resolved from the container.
/// </summary>
public sealed class DispatcherTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GreetCommandHandlerCallLog>();
        services.AddNexusApplication(typeof(DispatcherTests).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_ResolvesAndCallsTheRegisteredCommandHandler()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new GreetCommand("Nexus"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Hello, Nexus!");
    }

    [Fact]
    public async Task Send_ResolvesAndCallsTheRegisteredQueryHandler()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new DoubleQuery(21), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Send_WhenValidatorFails_ReturnsValidationFailureAndDoesNotCallHandler()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var callLog = provider.GetRequiredService<GreetCommandHandlerCallLog>();

        var result = await dispatcher.Send(new GreetCommand(string.Empty), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldBe("validation");
        result.Error.Message.ShouldContain("Name");
        callLog.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Send_WhenValidatorPasses_CallsTheHandler()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var callLog = provider.GetRequiredService<GreetCommandHandlerCallLog>();

        await dispatcher.Send(new GreetCommand("Nexus"), TestContext.Current.CancellationToken);

        callLog.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Send_WhenNoValidatorIsRegistered_CallsTheHandlerDirectly()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new UnvalidatedCommand(5), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(10);
    }

    [Fact]
    public async Task Send_WhenNoHandlerIsRegistered_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        // Deliberately register the dispatcher without scanning any assembly, so no handler
        // exists for UnregisteredCommand - a missing handler is a programming error, not a
        // business outcome, so Result.Failure would be the wrong signal here.
        services.AddNexusApplication();
        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.Send(new UnregisteredCommand()));
    }

    private sealed record UnregisteredCommand : ICommand<string>;
}
