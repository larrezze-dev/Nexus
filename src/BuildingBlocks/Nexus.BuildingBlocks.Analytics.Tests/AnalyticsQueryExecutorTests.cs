using System.Data;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Analytics.Querying;
using Nexus.BuildingBlocks.Analytics.Scope;
using Shouldly;

namespace Nexus.BuildingBlocks.Analytics.Tests;

/// <summary>
/// <see cref="AnalyticsQueryExecutor"/> and <see cref="AnalyticsStatement"/>: the scope predicate is
/// mandatory, injected as a bound parameter, and an empty scope never reaches ClickHouse
/// (conventions-api.md §7.3). Uses a real ClickHouse; filters are built with the internal
/// constructor (production code only gets them from <see cref="IScopeFilterProvider"/>).
/// </summary>
public sealed class AnalyticsQueryExecutorTests(ClickHouseFixture clickHouse) : IClassFixture<ClickHouseFixture>
{
    private const string CountByTypeSql =
        "SELECT count() FROM nexus_analytics.event_log FINAL WHERE aggregate_type = {aggregate_type:String} AND {__scope_predicate}";

    private static readonly Guid LeA = Guid.CreateVersion7();
    private static readonly Guid LeB = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "§7.3: a statement without the scope placeholder is rejected")]
    public void Statement_WithoutPlaceholder_Throws()
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement("SELECT count() FROM nexus_analytics.event_log"))
            .Message.ShouldContain(AnalyticsStatement.ScopePlaceholder);
    }

    [Fact(DisplayName = "§7.3: the scope parameter is reserved (no hand-bound or hand-written __scope)")]
    public void Statement_ReservedScopeNames_Throw()
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT 1 FROM t WHERE legal_entity_id IN {__scope_le:Array(UUID)} AND {__scope_predicate}"));
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT 1 FROM t WHERE {__scope_predicate} AND x = {__scope_x:String}",
            new Dictionary<string, object> { ["__scope_x"] = "x" }));
    }

    [Fact(DisplayName = "§7.3: parameters must be referenced as {name:Type} and non-null")]
    public void Statement_ParameterRules()
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT 1 FROM t WHERE {__scope_predicate}", new Dictionary<string, object> { ["unused"] = 1 }));
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT 1 FROM t WHERE {__scope_predicate} AND a = {a:String}", new Dictionary<string, object> { ["a"] = null! }));
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT 1 FROM t WHERE {__scope_predicate} AND a = {a b:String}", new Dictionary<string, object> { ["a b"] = "x" }));
    }

    [Fact(DisplayName = "§7.3: an empty scope filter returns the empty result without contacting ClickHouse")]
    public async Task EmptyFilter_NeverCallsClickHouse()
    {
        var factory = new CountingFactory();
        var executor = new AnalyticsQueryExecutor(factory);
        var query = new CountQuery("x");

        var result = await executor.ExecuteAsync(query, new ScopeFilter([], includeUnscoped: false), Ct);

        result.ShouldBe(-1);
        factory.Created.ShouldBe(0);
        query.Executed.ShouldBeFalse();
    }

    [Fact(DisplayName = "§7.3: a null scope filter is rejected")]
    public async Task NullFilter_Throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => clickHouse.CreateExecutor().ExecuteAsync(new CountQuery("x"), null!, Ct));
    }

    [Fact(DisplayName = "§7.3: the predicate keeps only the filter's LEs; NULL-LE rows only with IncludeUnscoped")]
    public async Task Predicate_FiltersByLegalEntity()
    {
        var type = "probe-" + Guid.NewGuid().ToString("N")[..8];
        await clickHouse.InsertAsync(
            EventLogRow.Create(LeA, type, Guid.CreateVersion7(), 1, "{}"),
            EventLogRow.Create(LeB, type, Guid.CreateVersion7(), 1, "{}"),
            EventLogRow.Create(null, type, Guid.CreateVersion7(), 1, "{}"));
        var executor = clickHouse.CreateExecutor();

        (await executor.ExecuteAsync(new CountQuery(type), new ScopeFilter([LeA], false), Ct)).ShouldBe(1);
        (await executor.ExecuteAsync(new CountQuery(type), new ScopeFilter([LeA, LeB], false), Ct)).ShouldBe(2);
        (await executor.ExecuteAsync(new CountQuery(type), new ScopeFilter([LeA], true), Ct)).ShouldBe(2);
        (await executor.ExecuteAsync(new CountQuery(type), new ScopeFilter([LeA, LeB], true), Ct)).ShouldBe(3);
        (await executor.ExecuteAsync(new CountQuery(type), new ScopeFilter([Guid.CreateVersion7()], false), Ct)).ShouldBe(0);

        // GLOBAL holder in a database with no legal entity yet: only the unscoped rows.
        (await executor.ExecuteAsync(new CountQuery(type), new ScopeFilter([], true), Ct)).ShouldBe(1);
    }

    [Fact(DisplayName = "§7.3: values are bound, never concatenated (an injection string is just a literal value)")]
    public async Task Values_AreBound()
    {
        const string Malicious = "x') OR 1=1 --";
        var executor = clickHouse.CreateExecutor();
        var everything = new ScopeFilter([LeA, LeB], includeUnscoped: true);

        await clickHouse.InsertAsync(EventLogRow.Create(LeA, "bound-probe", Guid.CreateVersion7(), 1, "{}"));

        // As a filter value it matches nothing (no row has that type) and raises no error...
        (await executor.ExecuteAsync(new CountQuery(Malicious), everything, Ct)).ShouldBe(0);

        // ...and once a row literally has that type, it matches exactly that row.
        await clickHouse.InsertAsync(EventLogRow.Create(LeA, Malicious, Guid.CreateVersion7(), 1, "{}"));
        (await executor.ExecuteAsync(new CountQuery(Malicious), everything, Ct)).ShouldBe(1);
    }

    [Fact(DisplayName = "§7.3: AddNexusAnalytics is lazy: resolves without ClickHouse config, fails only on the first scoped query")]
    public async Task Di_IsLazy()
    {
        var services = new ServiceCollection();
        services.AddNexusAnalytics(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var executor = provider.GetRequiredService<IAnalyticsQueryExecutor>();

        // An empty scope still answers (no ClickHouse needed)...
        (await executor.ExecuteAsync(new CountQuery("x"), new ScopeFilter([], false), Ct)).ShouldBe(-1);

        // ...a real one needs the configuration, and says which key is missing.
        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(new CountQuery("x"), new ScopeFilter([LeA], false), Ct));
        error.Message.ShouldContain("Nexus:ClickHouse:ConnectionString");
    }

    [Fact(DisplayName = "§7.3: AddNexusAnalytics reads Nexus:ClickHouse:ConnectionString and queries ClickHouse")]
    public async Task Di_UsesConfiguredConnection()
    {
        var services = new ServiceCollection();
        services.AddNexusAnalytics(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Nexus:ClickHouse:ConnectionString"] = clickHouse.ConnectionString })
            .Build());
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IAnalyticsQueryExecutor>()
            .ExecuteAsync(new CountQuery("never-" + Guid.NewGuid().ToString("N")), new ScopeFilter([LeA], false), Ct);
        result.ShouldBe(0);
    }

    /// <summary>Counts event_log rows of one aggregate type in scope; -1 as the empty result.</summary>
    private sealed class CountQuery(string aggregateType) : IAnalyticsQuery<long>
    {
        public bool Executed { get; private set; }

        public long EmptyResult => -1;

        public async Task<long> ExecuteAsync(IAnalyticsSession session, CancellationToken cancellationToken)
        {
            Executed = true;
            var statement = new AnalyticsStatement(CountByTypeSql, new Dictionary<string, object> { ["aggregate_type"] = aggregateType });
            var rows = await session.QueryAsync(statement, (IDataRecord r) => checked((long)(ulong)r.GetValue(0)), cancellationToken);
            return rows.Single();
        }
    }

    private sealed class CountingFactory : IAnalyticsConnectionFactory
    {
        public int Created { get; private set; }

        public ClickHouseConnection Create()
        {
            Created++;
            throw new InvalidOperationException("ClickHouse must not be contacted for an empty scope.");
        }
    }
}
