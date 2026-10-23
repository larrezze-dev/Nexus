using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Wires up the building blocks from this assembly into DI: the dispatcher, every
/// <see cref="ICommandHandler{TCommand,TResult}"/>/<see cref="IQueryHandler{TQuery,TResult}"/>
/// and <see cref="IValidator{T}"/> found in the given assemblies, and <see cref="IIdGenerator"/>.
/// No Scrutor (not in the approved package list, conventions-backend.md §10): scanning is plain
/// reflection over <see cref="Assembly.GetTypes"/>, matching closed generic interfaces against
/// their open generic definitions.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNexusApplication(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        services.AddScoped<IDispatcher, Dispatcher>();

        // TimeProvider.System is the sane default for a host that has not registered its own
        // (e.g. a FakeTimeProvider in tests); TryAdd so an existing registration always wins.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IIdGenerator, UuidV7IdGenerator>();

        foreach (var assembly in assemblies)
        {
            RegisterClosedImplementations(services, assembly, typeof(ICommandHandler<,>), ServiceLifetime.Scoped);
            RegisterClosedImplementations(services, assembly, typeof(IQueryHandler<,>), ServiceLifetime.Scoped);

            // Validators are stateless (rules are defined once in the constructor and do not
            // capture per-request state), so Singleton avoids an allocation per dispatch; this
            // matches how FluentValidation.DependencyInjectionExtensions' own AddValidatorsFromAssembly
            // defaults validator registrations.
            RegisterClosedImplementations(services, assembly, typeof(IValidator<>), ServiceLifetime.Singleton);
        }

        return services;
    }

    private static void RegisterClosedImplementations(
        IServiceCollection services,
        Assembly assembly,
        Type openGenericInterface,
        ServiceLifetime lifetime)
    {
        foreach (var implementationType in GetLoadableTypes(assembly))
        {
            if (!implementationType.IsClass || implementationType.IsAbstract)
            {
                continue;
            }

            foreach (var implementedInterface in implementationType.GetInterfaces())
            {
                if (!implementedInterface.IsGenericType ||
                    implementedInterface.GetGenericTypeDefinition() != openGenericInterface)
                {
                    continue;
                }

                services.Add(new ServiceDescriptor(implementedInterface, implementationType, lifetime));
            }
        }
    }

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> throws <see cref="ReflectionTypeLoadException"/> if any
    /// single type in the assembly fails to load; fall back to the types that did load instead
    /// of aborting the whole scan.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }
}
