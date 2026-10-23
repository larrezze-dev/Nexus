using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Application.Tests;

/// <summary>
/// Verifies <see cref="ServiceCollectionExtensions.AddNexusApplication"/> finds and registers
/// the handlers/validators declared in <see cref="TestFixtures"/> purely by scanning the given
/// assembly - no manual registration of those test types happens anywhere in this file.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNexusApplication_RegistersIDispatcher()
    {
        var services = new ServiceCollection();

        services.AddNexusApplication(typeof(ServiceCollectionExtensionsTests).Assembly);
        using var provider = services.BuildServiceProvider();

        provider.GetService<IDispatcher>().ShouldNotBeNull();
    }

    [Fact]
    public void AddNexusApplication_ScansAssemblyAndRegistersCommandHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GreetCommandHandlerCallLog>();

        services.AddNexusApplication(typeof(ServiceCollectionExtensionsTests).Assembly);
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetService<ICommandHandler<GreetCommand, string>>();
        handler.ShouldNotBeNull();
        handler.ShouldBeOfType<GreetCommandHandler>();
    }

    [Fact]
    public void AddNexusApplication_ScansAssemblyAndRegistersQueryHandler()
    {
        var services = new ServiceCollection();

        services.AddNexusApplication(typeof(ServiceCollectionExtensionsTests).Assembly);
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetService<IQueryHandler<DoubleQuery, int>>();
        handler.ShouldNotBeNull();
        handler.ShouldBeOfType<DoubleQueryHandler>();
    }

    [Fact]
    public void AddNexusApplication_ScansAssemblyAndRegistersValidator()
    {
        var services = new ServiceCollection();

        services.AddNexusApplication(typeof(ServiceCollectionExtensionsTests).Assembly);
        using var provider = services.BuildServiceProvider();

        var validator = provider.GetService<FluentValidation.IValidator<GreetCommand>>();
        validator.ShouldNotBeNull();
        validator.ShouldBeOfType<GreetCommandValidator>();
    }

    [Fact]
    public void AddNexusApplication_WhenNoAssembliesGiven_StillRegistersDispatcherAndIdGenerator()
    {
        var services = new ServiceCollection();

        services.AddNexusApplication();
        using var provider = services.BuildServiceProvider();

        provider.GetService<IDispatcher>().ShouldNotBeNull();
        provider.GetService<IIdGenerator>().ShouldNotBeNull();
    }
}
