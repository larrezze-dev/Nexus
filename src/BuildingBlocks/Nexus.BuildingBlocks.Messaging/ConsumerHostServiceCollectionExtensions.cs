using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Messaging.Retry;
using Nexus.BuildingBlocks.Persistence;

namespace Nexus.BuildingBlocks.Messaging;

/// <summary>ConsumerHost (step 0.7 track S): registration of a host's consumers by assembly scan, and their health.</summary>
public static class ConsumerHostServiceCollectionExtensions
{
    /// <summary>
    /// Registers every non-abstract <see cref="NexusConsumer{TEvent}"/> subclass found in
    /// <paramref name="assemblies"/> as a hosted service, plus what they share: the
    /// <see cref="NexusConsumerRuntime"/>, <see cref="NexusKafkaOptions"/> (section <c>Nexus:Kafka</c>),
    /// the DLQ publisher (<see cref="KafkaDlqPublisher"/>), <see cref="RetryOptions"/>, the
    /// <see cref="ConsumerHealthRegistry"/> and consumer idempotency (<see cref="InboxServiceCollectionExtensions.AddNexusInbox"/>).
    /// <para>
    /// <b>Order matters</b> (ADR-0017): call it AFTER <c>AddNexusApplication</c>, <c>AddNexusPersistence</c>
    /// and, in hosts that also serve HTTP, <c>AddNexusAuthentication</c>. It wraps the
    /// <see cref="ICurrentUser"/> registered at that point; an <see cref="ICurrentUser"/> registered later
    /// with <c>TryAdd</c> is silently ignored (HTTP requests then fail closed as unauthenticated), and a
    /// later plain <c>Add</c> would replace the wrapper (consumer scopes then fail at run time:
    /// <see cref="InboxProcessor"/> refuses to begin). A missing <c>AddNexusPersistence</c> is detected here.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNexusConsumers(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        if (!services.Any(d => d.ServiceType == typeof(IUnitOfWork)))
        {
            throw new InvalidOperationException(
                "AddNexusConsumers requires AddNexusApplication and AddNexusPersistence to be called first (the inbox drives the scope's IUnitOfWork).");
        }

        // Same default clock as AddNexusApplication (TryAdd: an earlier registration always wins).
        services.TryAddSingleton<TimeProvider>(RoundedSystemTimeProvider.Instance);
        services.AddOptions<NexusKafkaOptions>().BindConfiguration(NexusKafkaOptions.SectionName);
        services.TryAddSingleton(new RetryOptions());
        services.TryAddSingleton<ConsumerHealthRegistry>();
        services.TryAddSingleton<IDlqPublisher>(sp =>
        {
            var kafka = sp.GetRequiredService<IOptions<NexusKafkaOptions>>().Value;
            return new KafkaDlqPublisher(kafka.BootstrapServers, sp.GetRequiredService<TimeProvider>(), kafka.DlqMessageTimeout);
        });
        services.TryAddSingleton<NexusConsumerRuntime>();
        services.AddNexusInbox();

        foreach (var type in assemblies.SelectMany(ConsumerTypes))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IHostedService), type));
        }

        return services;
    }

    /// <summary>
    /// Adds <see cref="ConsumerLagHealthCheck"/> (name <c>kafka-consumers</c>) to the host's health checks,
    /// with thresholds from <c>Nexus:Consumers:Health</c>. Expose it with <c>app.MapHealthChecks("/health")</c>.
    /// Requires <see cref="AddNexusConsumers"/>.
    /// </summary>
    public static IHealthChecksBuilder AddNexusConsumerHealth(
        this IHealthChecksBuilder builder, string name = "kafka-consumers", IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<ConsumerHealthOptions>().BindConfiguration(ConsumerHealthOptions.SectionName);
        services.TryAddSingleton<ConsumerHealthRegistry>();
        services.TryAddSingleton<IConsumerLagProbe>(sp => new KafkaConsumerLagProbe(
            sp.GetRequiredService<IOptions<NexusKafkaOptions>>().Value.BootstrapServers,
            sp.GetRequiredService<IOptions<ConsumerHealthOptions>>().Value.QueryTimeout,
            sp.GetRequiredService<TimeProvider>()));
        // Singleton so the ≥ 5 s cache survives between probes (AddCheck<T> resolves T from DI when registered).
        services.TryAddSingleton<ConsumerLagHealthCheck>();
        return builder.AddCheck<ConsumerLagHealthCheck>(name, failureStatus: null, tags: tags ?? ["ready"]);
    }

    internal static IEnumerable<Type> ConsumerTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && IsNexusConsumer(t));

    private static bool IsNexusConsumer(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(NexusConsumer<>))
            {
                return true;
            }
        }

        return false;
    }
}
