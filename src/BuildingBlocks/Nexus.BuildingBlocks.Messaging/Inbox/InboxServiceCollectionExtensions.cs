using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;

namespace Nexus.BuildingBlocks.Messaging.Inbox;

/// <summary>DI wiring of consumer idempotency (conventions-events.md §7).</summary>
public static class InboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ConsumerIdentity"/> (scoped),
    /// the scoped <see cref="CorrelationContext"/>/<see cref="ICorrelationContext"/> pair if absent,
    /// and wraps the host's <see cref="ICurrentUser"/> so that a consumer scope runs as the event's
    /// identity while every other scope (HTTP requests) keeps the original implementation.
    /// <para>
    /// The inbox processor itself is deliberately NOT registered (ADR-0017): only
    /// <see cref="NexusConsumer{TEvent}"/> instantiates it, once per attempt, in its own scope.
    /// </para>
    /// <para>
    /// <b>Order matters:</b> call it after <c>AddNexusApplication</c> (<see cref="TimeProvider"/>),
    /// <c>AddNexusPersistence</c> and after any other
    /// <see cref="ICurrentUser"/> registration (<c>AddNexusAuthentication</c> in HTTP services).
    /// Those use <c>TryAdd</c>, so registering this first would silently stop them from
    /// registering theirs. A miswired scope is detected at run time: the processor refuses to
    /// begin if <see cref="ICurrentUser"/> does not reflect the consumer identity.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNexusInbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(d => d.ServiceType == typeof(ConsumerIdentity)))
        {
            return services; // idempotent
        }

        services.AddScoped<ConsumerIdentity>();
        services.TryAddScoped<CorrelationContext>();
        services.TryAddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());
        // TimeProvider deliberately not registered here: AddNexusApplication's microsecond-rounded
        // clock must win (a plain TimeProvider.System registered first would break Avro timestamps).

        var inner = services.LastOrDefault(d => d.ServiceType == typeof(ICurrentUser));
        if (inner is { IsKeyedService: true })
        {
            throw new InvalidOperationException("A keyed ICurrentUser registration is not supported by AddNexusInbox.");
        }

        services.RemoveAll<ICurrentUser>();
        services.AddScoped<ICurrentUser>(sp => new ConsumerAwareCurrentUser(
            sp.GetRequiredService<ConsumerIdentity>(),
            inner is null ? null : CreateInner(sp, inner)));

        return services;
    }

    private static ICurrentUser CreateInner(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ICurrentUser instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (ICurrentUser)factory(sp);
        }

        return (ICurrentUser)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}
