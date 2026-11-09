using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Analytics.Audit;
using Nexus.BuildingBlocks.Analytics.Querying;
using Nexus.BuildingBlocks.Analytics.Scope;

namespace Nexus.BuildingBlocks.Analytics;

/// <summary>DI wiring of the analytics building block.</summary>
public static class AnalyticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ClickHouse gateway of a service (conventions-api.md §7):
    /// <list type="bullet">
    ///   <item><see cref="NexusAnalyticsOptions"/> from <c>Nexus:ClickHouse</c> in
    ///   <paramref name="configuration"/> (read lazily, on the first query).</item>
    ///   <item><see cref="IAnalyticsQueryExecutor"/> (singleton; opens one ClickHouse connection
    ///   per query execution, lazily).</item>
    ///   <item><see cref="IScopeFilterProvider"/> (scoped, on the request's <c>IUnitOfWork</c>).</item>
    ///   <item><see cref="IAuditTrailReader"/> (scoped): the audit query over
    ///   <c>nexus_analytics.event_log</c>.</item>
    /// </list>
    /// Requires <c>AddNexusPersistence</c> (<c>IUnitOfWork</c>). Nothing here touches ClickHouse
    /// or validates its configuration at startup.
    /// </summary>
    public static IServiceCollection AddNexusAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(NexusAnalyticsOptions.SectionName);
        services.AddOptions<NexusAnalyticsOptions>().Configure(options =>
        {
            options.ConnectionString = section[nameof(NexusAnalyticsOptions.ConnectionString)];
            options.Password = section[nameof(NexusAnalyticsOptions.Password)];
            if (section[nameof(NexusAnalyticsOptions.QueryTimeout)] is { Length: > 0 } timeout)
            {
                options.QueryTimeout = ParseTimeout(timeout);
            }
        });

        services.TryAddSingleton<IAnalyticsConnectionFactory>(
            sp => new AnalyticsConnectionFactory(sp.GetRequiredService<IOptions<NexusAnalyticsOptions>>()));
        services.TryAddSingleton<IAnalyticsQueryExecutor, AnalyticsQueryExecutor>();
        services.TryAddScoped<IScopeFilterProvider, ScopeFilterProvider>();
        services.TryAddScoped<IAuditTrailReader, AuditTrailReader>();

        return services;
    }

    /// <summary>"30" / "1.5" (seconds) or a TimeSpan ("00:00:30"), invariant culture.</summary>
    private static TimeSpan ParseTimeout(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan)
                ? timeSpan
                : throw new InvalidOperationException(
                    $"Invalid '{NexusAnalyticsOptions.SectionName}:{nameof(NexusAnalyticsOptions.QueryTimeout)}' value '{value}': use seconds or hh:mm:ss.");
}
