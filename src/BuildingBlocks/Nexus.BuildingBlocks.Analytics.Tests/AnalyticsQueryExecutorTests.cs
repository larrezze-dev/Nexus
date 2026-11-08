using System.Data;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Analytics.Querying;
using Nexus.BuildingBlocks.Analytics.Scope;
using Nexus.BuildingBlocks.Persistence;
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

    [Theory(DisplayName = "§7.3: comments are rejected (a commented-out placeholder must not pass validation)")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log -- WHERE {__scope_predicate}")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log /* WHERE {__scope_predicate} */")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log # WHERE {__scope_predicate}")]
    public void Statement_WithComment_Throws(string sql)
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(sql)).Message.ShouldContain("comments");
    }

    [Theory(DisplayName = "§7.3: every nexus_analytics table reference needs its own placeholder (UNION/subquery cannot escape)")]
    [InlineData("SELECT legal_entity_id FROM nexus_analytics.dim_warehouse WHERE {__scope_predicate} UNION ALL SELECT legal_entity_id FROM nexus_analytics.dim_legal_entity")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} AND aggregate_id IN (SELECT warehouse_id FROM NEXUS_ANALYTICS.dim_warehouse)")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} AND aggregate_id IN (SELECT warehouse_id FROM `nexus_analytics`.`dim_warehouse`)")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} AND aggregate_id IN (SELECT warehouse_id FROM \"nexus_analytics\" . dim_warehouse)")]
    public void Statement_FewerPlaceholdersThanProjections_Throws(string sql)
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(sql)).Message.ShouldContain("nexus_analytics");
    }

    [Theory(DisplayName = "§7.3: Identifier parameters are rejected (a bound table name would hide the reference)")]
    [InlineData("SELECT count() FROM {db:Identifier}.event_log WHERE {__scope_predicate}")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} UNION ALL SELECT count() FROM {t: identifier}")]
    public void Statement_IdentifierParameter_Throws(string sql)
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(sql)).Message.ShouldContain("Identifier");
    }

    [Theory(DisplayName = "§7.3: table functions that read tables by name are rejected (case-insensitive, space before '(')")]
    [InlineData("merge('nexus_analytics', '^event_log$')")]
    [InlineData("MERGE ('nexus_analytics', '^event_log$')")]
    [InlineData("cluster('default', 'nexus_analytics', 'event_log')")]
    [InlineData("clusterAllReplicas('default', 'nexus_analytics.event_log')")]
    [InlineData("remote('localhost', 'nexus_analytics', 'event_log')")]
    [InlineData("remoteSecure('localhost', 'nexus_analytics.event_log')")]
    [InlineData("view (SELECT 1)")]
    [InlineData("loop('nexus_analytics', 'event_log')")]
    [InlineData("url('http://x', CSV)")]
    [InlineData("file('x.csv')")]
    [InlineData("s3('http://x')")]
    [InlineData("input('x String')")]
    public void Statement_TableFunction_Throws(string tableFunction)
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
                "SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} UNION ALL SELECT count() FROM " + tableFunction))
            .Message.ShouldContain("Table function");
    }

    [Fact(DisplayName = "§7.3: nexus_analytics inside a string literal is rejected; -Merge aggregate combinators are not table functions")]
    public void Statement_QuotedDatabase_Throws_CombinatorsAccepted()
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} AND someFunction('nexus_analytics', 'event_log') = 0"));

        _ = new AnalyticsStatement("SELECT argMaxMerge(x), uniqMerge(y), sumMerge(z) FROM nexus_analytics.agg WHERE {__scope_predicate}");
    }

    [Fact(DisplayName = "§7.3: the connection always uses database 'default': an unqualified projection fails with UNKNOWN_TABLE even if the config says Database=nexus_analytics")]
    public async Task UnqualifiedProjection_FailsEvenWithConfiguredDatabase()
    {
        var configured = new ClickHouseConnectionStringBuilder(clickHouse.ConnectionString) { Database = "nexus_analytics" }.ConnectionString;
        using var factory = new AnalyticsConnectionFactory(Microsoft.Extensions.Options.Options.Create(new NexusAnalyticsOptions { ConnectionString = configured }));

        // Passes AnalyticsStatement's count (one qualified reference, one placeholder), so only the
        // forced database stops the unfiltered branch.
        var escaping = new RawQuery(
            "SELECT legal_entity_id FROM nexus_analytics.event_log WHERE {__scope_predicate} " +
            "UNION ALL SELECT legal_entity_id FROM event_log");

        var error = await Should.ThrowAsync<Exception>(
            () => new AnalyticsQueryExecutor(factory).ExecuteAsync(escaping, new ScopeFilter([LeA], true), Ct));
        error.Message.ShouldContain("UNKNOWN_TABLE");

        // The qualified, filtered form still works on the same connection settings.
        (await new AnalyticsQueryExecutor(factory).ExecuteAsync(new CountQuery("never"), new ScopeFilter([LeA], false), Ct)).ShouldBe(0);
    }

    [Theory(DisplayName = "§7.3: string literals are tokenized: legitimate SQL with literals next to projection references is accepted")]
    [InlineData("SELECT jsonMergePatch(payload_json, '{}') FROM nexus_analytics.event_log WHERE {__scope_predicate}")]
    [InlineData("SELECT toDateTime(occurred_at, 'UTC') FROM nexus_analytics.event_log WHERE {__scope_predicate} UNION ALL SELECT toDateTime(_ingested_at, 'UTC') FROM nexus_analytics.dim_warehouse WHERE {__scope_predicate}")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} AND event_type <> 'x' AND aggregate_id IN (SELECT warehouse_id FROM nexus_analytics.dim_warehouse WHERE {__scope_predicate})")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} AND event_type IN ('a--b#c', 'file(x)', 'settings.updated', 'it''s', '/*')")]
    public void Statement_Literals_DoNotCauseFalsePositives(string sql)
    {
        _ = new AnalyticsStatement(sql);
    }

    [Fact(DisplayName = "§7.3: a placeholder inside a string literal does not count")]
    public void Statement_PlaceholderInLiteral_DoesNotCount()
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT count() FROM nexus_analytics.event_log WHERE event_type <> '{__scope_predicate}'")).Message.ShouldContain("outside string literals");
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} UNION ALL " +
            "SELECT count() FROM nexus_analytics.dim_warehouse WHERE x = '{__scope_predicate}'")).Message.ShouldContain("placeholder(s)");
    }

    [Theory(DisplayName = "§7.3: table-function names quoted as identifiers are rejected too")]
    [InlineData("`merge`(currentDatabase(), '^event_log$')")]
    [InlineData("\"merge\" (currentDatabase(), '^event_log$')")]
    [InlineData("`remote`('localhost', currentDatabase(), 'event_log')")]
    public void Statement_QuotedTableFunction_Throws(string tableFunction)
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
                "SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} UNION ALL SELECT count() FROM " + tableFunction))
            .Message.ShouldContain("Table function");
    }

    [Theory(DisplayName = "§7.3: backslashes (escape sequences) are rejected in identifiers and literals")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} UNION ALL SELECT count() FROM `\\x6Eexus_analytics`.event_log")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} AND event_type <> 'a\\'b'")]
    public void Statement_Backslash_Throws(string sql)
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(sql)).Message.ShouldContain("backslash");
    }

    [Theory(DisplayName = "§7.3: SETTINGS clauses are rejected (settings belong to the executor)")]
    [InlineData("SELECT count() FROM nexus_analytics.event_log WHERE {__scope_predicate} SETTINGS max_threads = 1")]
    [InlineData("SELECT count() FROM event_log WHERE {__scope_predicate} settings database = 'x'")]
    public void Statement_Settings_Throws(string sql)
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(sql)).Message.ShouldContain("SETTINGS");
    }

    [Fact(DisplayName = "§7.3: the real audit statements pass validation")]
    public void AuditStatements_PassValidation()
    {
        _ = new Nexus.BuildingBlocks.Analytics.Audit.AuditEventQuery(
            "legal-entity", Guid.CreateVersion7(), 3, 50, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    [Fact(DisplayName = "§7.3: one placeholder per projection reference is accepted")]
    public void Statement_PlaceholderPerProjection_IsAccepted()
    {
        _ = new AnalyticsStatement(
            "SELECT legal_entity_id FROM nexus_analytics.dim_warehouse WHERE {__scope_predicate} " +
            "UNION ALL SELECT legal_entity_id FROM Nexus_Analytics.dim_legal_entity WHERE {__scope_predicate}");
        _ = new AnalyticsStatement("SELECT 1 FROM (SELECT toUUID('00000000-0000-0000-0000-000000000000') AS legal_entity_id) WHERE {__scope_predicate}");
    }

    [Fact(DisplayName = "§7.3: parameter names reject a trailing newline (\\z, not $)")]
    public void Statement_ParameterNameWithTrailingNewline_Throws()
    {
        Should.Throw<ArgumentException>(() => new AnalyticsStatement(
            "SELECT 1 FROM t WHERE {__scope_predicate} AND a = {a\n:String}", new Dictionary<string, object> { ["a\n"] = "x" }));
    }

    [Fact(DisplayName = "§7.3: a statement over QueryTimeout is stopped by ClickHouse (max_execution_time) → TimeoutException")]
    public async Task SlowQuery_TimesOut()
    {
        var executor = clickHouse.CreateSlowQueryExecutor(TimeSpan.FromSeconds(1));
        var started = System.Diagnostics.Stopwatch.StartNew();

        var error = await Should.ThrowAsync<TimeoutException>(
            () => executor.ExecuteAsync(new SlowQuery(LeA, rows: 10, marker: "t" + Guid.NewGuid().ToString("N")), new ScopeFilter([LeA], false), Ct));

        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4)); // the query alone would take 5 s
        error.Message.ShouldContain("Nexus:ClickHouse:QueryTimeout");
        error.InnerException.ShouldNotBeNull();
    }

    [Fact(DisplayName = "§7.3: cancelling the CancellationToken aborts the call and ClickHouse stops the query")]
    public async Task Cancellation_PropagatesToClickHouse()
    {
        var marker = "c" + Guid.NewGuid().ToString("N");
        var executor = clickHouse.CreateSlowQueryExecutor();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var running = executor.ExecuteAsync(new SlowQuery(LeA, rows: 40, marker), new ScopeFilter([LeA], false), cts.Token); // ~20 s
        (await WaitForAsync(async () => await RunningCountAsync(marker) == 1, TimeSpan.FromSeconds(10))).ShouldBeTrue("the query never started");

        var started = System.Diagnostics.Stopwatch.StartNew();
        await cts.CancelAsync();
        var error = await Should.ThrowAsync<Exception>(() => running);
        error.ShouldBeAssignableTo<OperationCanceledException>();
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));

        // cancel_http_readonly_queries_on_client_close: the server drops it too.
        (await WaitForAsync(async () => await RunningCountAsync(marker) == 0, TimeSpan.FromSeconds(5))).ShouldBeTrue("ClickHouse kept running the cancelled query");
    }

    [Fact(DisplayName = "§7.3: the analytic path is read-only (readonly = 2): DDL or writes are rejected by ClickHouse")]
    public async Task AnalyticPath_IsReadOnly()
    {
        var table = "default.probe_" + Guid.NewGuid().ToString("N");
        var error = await Should.ThrowAsync<Exception>(() => clickHouse.CreateExecutor().ExecuteAsync(
            new RawQuery($"CREATE TABLE {table} ENGINE = Memory AS SELECT legal_entity_id FROM nexus_analytics.dim_warehouse WHERE {{__scope_predicate}}"),
            new ScopeFilter([LeA], false),
            Ct));
        error.Message.ShouldContain("readonly", Case.Insensitive);
    }

    [Fact(DisplayName = "§7.3: QueryTimeout is read from Nexus:ClickHouse:QueryTimeout (seconds or hh:mm:ss) and must be positive")]
    public async Task QueryTimeout_Configuration()
    {
        foreach (var (value, expected) in new[] { ("2", TimeSpan.FromSeconds(2)), ("00:01:00", TimeSpan.FromMinutes(1)) })
        {
            var services = new ServiceCollection();
            services.AddNexusAnalytics(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nexus:ClickHouse:ConnectionString"] = clickHouse.ConnectionString,
                ["Nexus:ClickHouse:QueryTimeout"] = value,
            }).Build());
            await using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IAnalyticsConnectionFactory>().QueryTimeout.ShouldBe(expected);
        }

        using var invalid = new AnalyticsConnectionFactory(Microsoft.Extensions.Options.Options.Create(
            new NexusAnalyticsOptions { ConnectionString = clickHouse.ConnectionString, QueryTimeout = TimeSpan.Zero }));
        Should.Throw<InvalidOperationException>(() => invalid.Create()).Message.ShouldContain("QueryTimeout");
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

    [Fact(DisplayName = "§7.3: binding is server-side: the SQL on the wire carries the placeholders, the values travel as param_*")]
    public async Task Binding_IsServerSide()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");
        var le = Guid.CreateVersion7();
        using var factory = new RecordingFactory(clickHouse.ConnectionString);

        (await new AnalyticsQueryExecutor(factory).ExecuteAsync(new CountQuery(marker), new ScopeFilter([le], false), Ct)).ShouldBe(0);

        // The request that ran the statement (the connection's own version probe is another one).
        var request = factory.Requests.Where(r => r.Contains("nexus_analytics.event_log", StringComparison.Ordinal)).ShouldHaveSingleItem();
        var unescaped = Uri.UnescapeDataString(request);

        // Client-side substitution would have replaced these with the values.
        unescaped.ShouldContain("aggregate_type = {aggregate_type:String}");
        unescaped.ShouldContain("(legal_entity_id IN {__scope_le:Array(UUID)})");

        // The values are sent as separate server-side parameters.
        unescaped.ShouldContain("param_aggregate_type");
        unescaped.ShouldContain("param___scope_le");
        unescaped.ShouldContain(marker);
        unescaped.ShouldContain(le.ToString());
    }

    [Fact(DisplayName = "§7.3: AddNexusAnalytics is lazy: resolves without ClickHouse config, fails only on the first scoped query")]
    public async Task Di_IsLazy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexusAnalytics(new ConfigurationBuilder().Build());
        services.AddScoped<IUnitOfWork>(_ => throw new InvalidOperationException("not needed here"));
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

    private async Task<ulong> RunningCountAsync(string marker)
    {
        await using var connection = await clickHouse.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count() FROM system.processes WHERE query LIKE {pattern:String} AND query NOT LIKE '%system.processes%'";
        command.AddParameter("pattern", "%" + marker + "%");
        return (ulong)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100, Ct);
        }

        return false;
    }

    /// <summary>~0.5 s per row; run it with CreateSlowQueryExecutor (one row per block). The marker is an alias, to find it in system.processes.</summary>
    private sealed class SlowQuery(Guid legalEntityId, int rows, string marker) : IAnalyticsQuery<long>
    {
        public long EmptyResult => -1;

        public async Task<long> ExecuteAsync(IAnalyticsSession session, CancellationToken cancellationToken)
        {
            var statement = new AnalyticsStatement(
                $"SELECT count() AS {marker} FROM (SELECT toUUID({{le:String}}) AS legal_entity_id, sleepEachRow(0.5) AS s FROM numbers({rows})) " +
                "WHERE {__scope_predicate} AND s = 0",
                new Dictionary<string, object> { ["le"] = legalEntityId.ToString() });
            var result = await session.QueryAsync(statement, (IDataRecord r) => checked((long)(ulong)r.GetValue(0)), cancellationToken);
            return result.Single();
        }
    }

    /// <summary>Runs one statement and ignores its rows.</summary>
    private sealed class RawQuery(string sql) : IAnalyticsQuery<int>
    {
        public int EmptyResult => -1;

        public async Task<int> ExecuteAsync(IAnalyticsSession session, CancellationToken cancellationToken) =>
            (await session.QueryAsync(new AnalyticsStatement(sql), (IDataRecord _) => 0, cancellationToken)).Count;
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

    /// <summary>Real connections whose HTTP requests (URI + body) are recorded.</summary>
    private sealed class RecordingFactory(string connectionString) : IAnalyticsConnectionFactory, IDisposable
    {
        private readonly RecordingHandler _handler = new();

        public IReadOnlyList<string> Requests => _handler.Requests;

        public TimeSpan QueryTimeout => NexusAnalyticsOptions.DefaultQueryTimeout;

        // Request compression off so the recorded body is readable text.
        public ClickHouseConnection Create() =>
            new(new ClickHouseConnectionStringBuilder(connectionString) { Compression = false }.ConnectionString, new HttpClient(_handler, disposeHandler: false));

        public void Dispose() => _handler.Dispose();

        private sealed class RecordingHandler() : DelegatingHandler(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            public List<string> Requests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                lock (Requests)
                {
                    Requests.Add(request.RequestUri + "\n" + body);
                }

                return await base.SendAsync(request, cancellationToken);
            }
        }
    }

    private sealed class CountingFactory : IAnalyticsConnectionFactory
    {
        public int Created { get; private set; }

        public TimeSpan QueryTimeout => NexusAnalyticsOptions.DefaultQueryTimeout;

        public ClickHouseConnection Create()
        {
            Created++;
            throw new InvalidOperationException("ClickHouse must not be contacted for an empty scope.");
        }
    }
}
