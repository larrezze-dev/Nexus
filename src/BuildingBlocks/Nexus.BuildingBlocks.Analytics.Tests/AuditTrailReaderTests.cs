using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Analytics.Audit;
using Nexus.BuildingBlocks.Persistence;
using Shouldly;

namespace Nexus.BuildingBlocks.Analytics.Tests;

/// <summary>
/// The audit query over <c>nexus_analytics.event_log</c> (ficha 0.8 track S items 5-6,
/// conventions-api.md §7 <c>[+]</c>, <c>contracts/openapi/_shared/audit.yaml</c>), end to end:
/// real PostgreSQL scope (AUDIT:READ via <c>access.has_permission</c>) + real ClickHouse.
/// LE-A is the CH legal entity (<c>usera</c>'s), LE-B the DE one.
/// </summary>
public sealed class AuditTrailReaderTests(AnalyticsFixture fixture) : IClassFixture<AnalyticsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid LeCh => fixture.Org.LegalEntityA.Id;

    private Guid LeDe => fixture.Org.LegalEntityB.Id;

    [Fact(DisplayName = "Audit of an LE-DE aggregate as usera → empty; as admin → complete")]
    public async Task LeDeAggregate_UserAEmpty_AdminComplete()
    {
        var warehouseId = Guid.CreateVersion7();
        await fixture.ClickHouse.InsertAsync(
            EventLogRow.Create(LeDe, "warehouse", warehouseId, 1, Payload(1), "created"),
            EventLogRow.Create(LeDe, "warehouse", warehouseId, 2, Payload(2)),
            EventLogRow.Create(LeDe, "warehouse", warehouseId, 3, Payload(3)));

        var asUserA = await fixture.ListAsAsync(fixture.Org.UserA.Id, "warehouse", warehouseId);
        asUserA.Items.ShouldBeEmpty();
        asUserA.Total.ShouldBe(0);

        var asAdmin = await fixture.ListAsAsync(fixture.Org.Admin.Id, "warehouse", warehouseId);
        asAdmin.Total.ShouldBe(3);
        asAdmin.Items.Select(i => i.AggregateVersion).ShouldBe([3L, 2L, 1L]);
        asAdmin.Items.Select(i => i.EventType).ShouldBe(["org.warehouse.updated", "org.warehouse.updated", "org.warehouse.created"]);

        // The DE legal entity's own admin sees it too.
        (await fixture.ListAsAsync(fixture.Org.UserB.Id, "warehouse", warehouseId)).Total.ShouldBe(3);
    }

    [Fact(DisplayName = "Rows with NULL legal_entity_id: visible only with AUDIT:READ in a GLOBAL assignment")]
    public async Task UnscopedRows_OnlyGlobal()
    {
        var userId = Guid.CreateVersion7();
        await fixture.ClickHouse.InsertAsync(
            EventLogRow.Create(null, "user", userId, 1, Payload(1), "created"),
            EventLogRow.Create(null, "user", userId, 2, Payload(2)));

        (await fixture.ListAsAsync(fixture.Org.Admin.Id, "user", userId)).Total.ShouldBe(2);

        foreach (var notGlobal in new[] { fixture.Org.UserA.Id, fixture.Org.UserB.Id, fixture.GroupViewer, fixture.Org.WhA.Id })
        {
            var page = await fixture.ListAsAsync(notGlobal, "user", userId);
            page.Items.ShouldBeEmpty();
            page.Total.ShouldBe(0);
        }
    }

    [Fact(DisplayName = "Intended semantics: AUDIT:READ only at WAREHOUSE/BUSINESS_UNIT scope sees nothing, even in its own LE")]
    public async Task BelowLegalEntityScope_SeesNothing()
    {
        // wha holds AUDIT:READ on WH-A, buviewer on BU-A; both are inside LE-A. The audit scope is
        // per legal entity (has_permission(AUDIT:READ, le)), which neither grant satisfies.
        var warehouseId = fixture.Org.WarehouseA;
        await fixture.ClickHouse.InsertAsync(EventLogRow.Create(LeCh, "warehouse", warehouseId, 1, Payload(1), "created"));

        foreach (var user in new[] { fixture.Org.WhA.Id, fixture.BuViewer })
        {
            var page = await fixture.ListAsAsync(user, "warehouse", warehouseId);
            page.Items.ShouldBeEmpty();
            page.Total.ShouldBe(0);
        }

        (await fixture.ListAsAsync(fixture.Org.UserA.Id, "warehouse", warehouseId)).Total.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Theory(DisplayName = "Order aggregateVersion DESC; previousPayload correct across page boundaries; total on every page")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Paging_PreviousPayloadAcrossBoundaries(int pageSize)
    {
        var id = Guid.CreateVersion7();
        await fixture.ClickHouse.InsertAsync(
            Enumerable.Range(1, 5).Select(v => EventLogRow.Create(LeCh, "legal-entity", id, (ulong)v, Payload(v))).ToArray());

        var all = new List<AuditEventRecord>();
        for (var page = 1; ; page++)
        {
            var result = await fixture.ListAsAsync(fixture.Org.UserA.Id, "legal-entity", id, page, pageSize);
            result.Total.ShouldBe(5);
            result.Page.ShouldBe(page);
            result.PageSize.ShouldBe(pageSize);
            result.Items.Count.ShouldBeLessThanOrEqualTo(pageSize);
            if (result.Items.Count == 0)
            {
                break;
            }

            all.AddRange(result.Items);
        }

        all.Select(i => i.AggregateVersion).ShouldBe([5L, 4L, 3L, 2L, 1L]);
        foreach (var item in all)
        {
            V(item.Payload).ShouldBe((int)item.AggregateVersion);
            if (item.AggregateVersion == 1)
            {
                item.PreviousPayload.ShouldBeNull();
            }
            else
            {
                V(item.PreviousPayload).ShouldBe((int)item.AggregateVersion - 1);
            }
        }
    }

    [Fact(DisplayName = "previousPayload is the previous VISIBLE version (an invisible version in between never leaks)")]
    public async Task PreviousPayload_SkipsInvisibleVersions()
    {
        var id = Guid.CreateVersion7();
        await fixture.ClickHouse.InsertAsync(
            EventLogRow.Create(LeCh, "probe-agg", id, 1, Payload(1)),
            EventLogRow.Create(LeDe, "probe-agg", id, 2, Payload(2)),
            EventLogRow.Create(LeCh, "probe-agg", id, 3, Payload(3)));

        var page = await fixture.ListAsAsync(fixture.Org.UserA.Id, "probe-agg", id, 1, 1);
        page.Total.ShouldBe(2);
        page.Items.ShouldHaveSingleItem().AggregateVersion.ShouldBe(3);
        V(page.Items[0].PreviousPayload).ShouldBe(1);

        var adminView = await fixture.ListAsAsync(fixture.Org.Admin.Id, "probe-agg", id);
        adminView.Items.Select(i => V(i.PreviousPayload)).ShouldBe([2, 1, (int?)null]);
    }

    [Theory(DisplayName = "PII rows (payload_json = '') → payload and previousPayload null; the next item's previousPayload is null too")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(50)]
    public async Task Pii_PayloadsAreNull(int pageSize)
    {
        var id = Guid.CreateVersion7();
        await fixture.ClickHouse.InsertAsync(
            EventLogRow.Create(LeCh, "party", id, 1, Payload(1), "created"),
            EventLogRow.Create(LeCh, "party", id, 2, string.Empty),
            EventLogRow.Create(LeCh, "party", id, 3, Payload(3)),
            EventLogRow.Create(LeCh, "party", id, 4, Payload(4)));

        var items = new List<AuditEventRecord>();
        for (var page = 1; page <= 4; page++)
        {
            items.AddRange((await fixture.ListAsAsync(fixture.Org.UserA.Id, "party", id, page, pageSize)).Items);
        }

        items.Select(i => i.AggregateVersion).ShouldBe([4L, 3L, 2L, 1L]);
        var byVersion = items.ToDictionary(i => i.AggregateVersion);

        V(byVersion[4].Payload).ShouldBe(4);
        V(byVersion[4].PreviousPayload).ShouldBe(3);

        V(byVersion[3].Payload).ShouldBe(3);
        byVersion[3].PreviousPayload.ShouldBeNull(); // points to the PII v2

        byVersion[2].Payload.ShouldBeNull();
        byVersion[2].PreviousPayload.ShouldBeNull(); // PII item: never shows a previous payload

        V(byVersion[1].Payload).ShouldBe(1);
        byVersion[1].PreviousPayload.ShouldBeNull();
    }

    [Fact(DisplayName = "Duplicate rows of the same event (unmerged ReplacingMergeTree parts) collapse via FINAL, in items and total")]
    public async Task Duplicates_CollapseWithFinal()
    {
        var id = Guid.CreateVersion7();
        var v1 = EventLogRow.Create(LeCh, "warehouse", id, 1, Payload(1), "created");
        var v2 = EventLogRow.Create(LeCh, "warehouse", id, 2, Payload(2));

        // Three separate inserts = three parts; merges are stopped in the fixture.
        await fixture.ClickHouse.InsertAsync(v1, v2);
        await fixture.ClickHouse.InsertAsync(v1 with { IngestedAt = DateTime.UtcNow.AddSeconds(1) });
        await fixture.ClickHouse.InsertAsync(v2 with { IngestedAt = DateTime.UtcNow.AddSeconds(2) });
        (await fixture.ClickHouse.RawCountAsync($"aggregate_id = '{id}'")).ShouldBe(4UL);

        var page = await fixture.ListAsAsync(fixture.Org.UserA.Id, "warehouse", id);
        page.Total.ShouldBe(2);
        page.Items.Select(i => i.EventId).ShouldBe([v2.EventId, v1.EventId]);
        V(page.Items[0].PreviousPayload).ShouldBe(1);
    }

    [Fact(DisplayName = "Fields map 1:1 from event_log (ids, eventType, version, occurredAt with µs, UTC)")]
    public async Task Fields_AreMapped()
    {
        var id = Guid.CreateVersion7();
        var row = EventLogRow.Create(LeCh, "legal-entity", id, 7, Payload(7));
        await fixture.ClickHouse.InsertAsync(row);

        var item = (await fixture.ListAsAsync(fixture.Org.UserA.Id, "legal-entity", id)).Items.ShouldHaveSingleItem();
        item.EventId.ShouldBe(row.EventId);
        item.EventType.ShouldBe("org.legal-entity.updated");
        item.AggregateVersion.ShouldBe(7);
        item.ActorUserId.ShouldBe(row.ActorUserId);
        item.CorrelationId.ShouldBe(row.CorrelationId);
        item.OccurredAt.ShouldBe(new DateTimeOffset(row.OccurredAt));
        item.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        item.Payload!.Value.GetProperty("code").GetString().ShouldBe("CODE-7");
        item.PreviousPayload.ShouldBeNull();
    }

    [Fact(DisplayName = "Unknown aggregate → empty list (not an error); aggregateType is matched literally (kebab vs snake)")]
    public async Task UnknownAggregate_Empty()
    {
        var id = Guid.CreateVersion7();
        await fixture.ClickHouse.InsertAsync(EventLogRow.Create(LeCh, "business_unit", id, 1, Payload(1), "created"));

        (await fixture.ListAsAsync(fixture.Org.Admin.Id, "business_unit", Guid.CreateVersion7())).Total.ShouldBe(0);
        (await fixture.ListAsAsync(fixture.Org.Admin.Id, "business-unit", id)).Total.ShouldBe(0);
        (await fixture.ListAsAsync(fixture.Org.Admin.Id, "business_unit", id)).Total.ShouldBe(1);
    }

    [Fact(DisplayName = "Input validation: injection-shaped aggregateType, page < 1, pageSize out of 1..200 → argument errors")]
    public async Task InputValidation()
    {
        await using var unitOfWork = await fixture.BeginAsync(fixture.Org.Admin.Id);
        var reader = fixture.Reader(unitOfWork);
        var id = Guid.CreateVersion7();

        await Should.ThrowAsync<ArgumentException>(() => reader.ListAsync("x') OR 1=1 --", id, 1, 50, Ct));
        await Should.ThrowAsync<ArgumentException>(() => reader.ListAsync("Warehouse", id, 1, 50, Ct));

        // .NET's $ would accept a trailing newline; the contract's ECMA pattern does not.
        await Should.ThrowAsync<ArgumentException>(() => reader.ListAsync("warehouse\n", id, 1, 50, Ct));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => reader.ListAsync("warehouse", id, 0, 50, Ct));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => reader.ListAsync("warehouse", id, 1, 0, Ct));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => reader.ListAsync("warehouse", id, 1, 201, Ct));
    }

    [Fact(DisplayName = "A non-JSON payload_json does not fail the page: payload null, newer item's previousPayload null, warning with the event id only")]
    public async Task InvalidJsonPayload_IsNullAndLoggedWithoutContent()
    {
        const string Garbage = "not json {secret-ish content";
        var id = Guid.CreateVersion7();
        var v1 = EventLogRow.Create(LeCh, "warehouse", id, 1, Payload(1), "created");
        var v2 = EventLogRow.Create(LeCh, "warehouse", id, 2, Garbage);
        var v3 = EventLogRow.Create(LeCh, "warehouse", id, 3, Payload(3));
        await fixture.ClickHouse.InsertAsync(v1, v2, v3);
        var logger = new CapturingLogger<AuditTrailReader>();

        var page = await fixture.ListAsAsync(fixture.Org.UserA.Id, "warehouse", id, logger: logger);

        page.Total.ShouldBe(3);
        var byVersion = page.Items.ToDictionary(i => i.AggregateVersion);
        V(byVersion[3].Payload).ShouldBe(3);
        byVersion[3].PreviousPayload.ShouldBeNull();
        byVersion[2].Payload.ShouldBeNull();
        byVersion[2].PreviousPayload.ShouldBeNull();
        V(byVersion[1].Payload).ShouldBe(1);

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(v2.EventId.ToString());
        warning.Message.ShouldNotContain("secret-ish");
        warning.Exception.ShouldBeNull();
    }

    [Fact(DisplayName = "DI: AddNexusAnalytics wires IAuditTrailReader on the request's IUnitOfWork")]
    public async Task Di_ResolvesReader()
    {
        var id = Guid.CreateVersion7();
        await fixture.ClickHouse.InsertAsync(EventLogRow.Create(LeDe, "warehouse", id, 1, Payload(1), "created"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexusAnalytics(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Nexus:ClickHouse:ConnectionString"] = fixture.ClickHouse.ConnectionString })
            .Build());
        await using var unitOfWork = await fixture.BeginAsync(fixture.Org.UserB.Id);
        services.AddScoped<IUnitOfWork>(_ => unitOfWork);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        var page = await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>().ListAsync("warehouse", id, 1, 50, Ct);
        page.Total.ShouldBe(1);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }

    private static string Payload(int v) => JsonSerializer.Serialize(new { v, code = $"CODE-{v}", isActive = true });

    private static int? V(JsonElement? payload) => payload?.GetProperty("v").GetInt32();
}
