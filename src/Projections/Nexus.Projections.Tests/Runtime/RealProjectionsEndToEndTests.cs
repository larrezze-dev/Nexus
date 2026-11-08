using Avro;
using Avro.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Testing;
using Nexus.Projections.Projections;
using Nexus.Projections.Tests.Runtime.Infrastructure;
using Shouldly;

namespace Nexus.Projections.Tests.Runtime;

/// <summary>
/// The REAL step 0.8 projections (registered through <c>AddNexusProjectionDefinitions</c>, real
/// <c>topics.yaml</c>, real org Avro schemas from contracts/events/org) run by the runtime against real
/// Kafka + Schema Registry + ClickHouse.
/// </summary>
[Collection(RuntimeTestGroup.Name)]
public sealed class RealProjectionsEndToEndTests(RuntimeEnvironment env)
{
    private const string LegalEntityCreated = "nexus.org.legal-entity.created";
    private const string PartyCreated = "nexus.org.party.created"; // pii: true in topics.yaml
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(40);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "event_log: the same event produced twice is one row with FINAL; dim_legal_entity gets the latest state")]
    public async Task Same_event_twice_is_one_event_log_row()
    {
        await CreateTopicsAsync();
        var legalEntity = Guid.CreateVersion7();
        var envelope = Envelope("org.legal-entity.created", "legal-entity", legalEntity, legalEntity: null);
        var payload = LegalEntityPayload(legalEntity);
        await ProduceAsync(LegalEntityCreated, envelope, payload);
        await ProduceAsync(LegalEntityCreated, envelope, payload);

        await using var h = RuntimeHarness.Create(env, [], realDefinitions: true);
        h.Start(h.Catalog.Find("event_log")!);
        h.Start(h.Catalog.Find("dim_legal_entity")!);

        (await Eventually.Until(async () => await env.ClickHouse.CountAsync(
            $"SELECT count() FROM nexus_analytics.event_log WHERE event_id = '{envelope.EventId}'") == 2, Wait)).ShouldBeTrue("both deliveries inserted");
        (await env.ClickHouse.CountAsync($"SELECT count() FROM nexus_analytics.event_log FINAL WHERE event_id = '{envelope.EventId}'")).ShouldBe(1);
        var row = (await env.ClickHouse.RowsAsync(
            $"SELECT legal_entity_id, event_type, aggregate_type, payload_json != '' FROM nexus_analytics.event_log FINAL WHERE event_id = '{envelope.EventId}'")).Single();
        // legal_entity_id derived from the aggregate (header empty for org events), payload kept (pii: false).
        row.ShouldBe($"{legalEntity} | org.legal-entity.created | legal-entity | True");

        (await Eventually.Until(async () => await env.ClickHouse.CountAsync(
            $"SELECT count() FROM nexus_analytics.dim_legal_entity FINAL WHERE legal_entity_id = '{legalEntity}'") == 1, Wait)).ShouldBeTrue();
        (await env.ClickHouse.RowsAsync($"SELECT code, currency_code, is_active FROM nexus_analytics.dim_legal_entity FINAL WHERE legal_entity_id = '{legalEntity}'"))
            .ShouldBe(["NUS-T | CHF | True"]);
    }

    [Fact(DisplayName = "EV-R5: an event of a pii: true topic (nexus.org.party.created) is logged with payload_json '' and its headers kept")]
    public async Task Pii_topic_keeps_only_headers()
    {
        await CreateTopicsAsync();
        var party = Guid.CreateVersion7();
        var envelope = Envelope("org.party.created", "party", party, legalEntity: null);
        await ProduceAsync(PartyCreated, envelope, PartyPayload(party));

        await using var h = RuntimeHarness.Create(env, [], realDefinitions: true);
        h.Start(h.Catalog.Find("event_log")!);

        (await Eventually.Until(async () => await env.ClickHouse.CountAsync(
            $"SELECT count() FROM nexus_analytics.event_log FINAL WHERE event_id = '{envelope.EventId}'") == 1, Wait)).ShouldBeTrue();
        var row = (await env.ClickHouse.RowsAsync(
            "SELECT topic, event_type, aggregate_type, aggregate_id, aggregate_version, actor_user_id, correlation_id, legal_entity_id, payload_json " +
            $"FROM nexus_analytics.event_log FINAL WHERE event_id = '{envelope.EventId}'")).Single();
        row.ShouldBe($"{PartyCreated} | org.party.created | party | {party} | 1 | {envelope.ActorUserId} | {envelope.CorrelationId} | NULL | ");
    }

    [Fact(DisplayName = "rebuild event_log: refused while the worker runs; otherwise truncates and reproduces exactly the same rows (all columns but _ingested_at)")]
    public async Task Rebuild_event_log_reproduces_rows()
    {
        await CreateTopicsAsync();
        var legalEntity = Guid.CreateVersion7();
        var party = Guid.CreateVersion7();
        await ProduceAsync(LegalEntityCreated, Envelope("org.legal-entity.created", "legal-entity", legalEntity, null), LegalEntityPayload(legalEntity));
        await ProduceAsync(LegalEntityCreated, Envelope("org.legal-entity.created", "legal-entity", legalEntity, null, version: 2), LegalEntityPayload(legalEntity));
        await ProduceAsync(PartyCreated, Envelope("org.party.created", "party", party, null), PartyPayload(party));
        var mine = $"aggregate_id IN ('{legalEntity}', '{party}')";
        var columns = "legal_entity_id, topic, event_id, event_type, aggregate_type, aggregate_id, aggregate_version, occurred_at, actor_user_id, correlation_id, payload_json";

        List<string> before;
        await using (var h = RuntimeHarness.Create(env, [], realDefinitions: true))
        {
            h.Start(h.Catalog.Find("event_log")!);
            (await Eventually.Until(async () => await env.ClickHouse.CountAsync($"SELECT count() FROM nexus_analytics.event_log FINAL WHERE {mine}") == 3, Wait)).ShouldBeTrue();
            before = await env.ClickHouse.RowsAsync($"SELECT {columns} FROM nexus_analytics.event_log FINAL WHERE {mine} ORDER BY event_id");

            // While the worker is a member of projections.event_log the rebuild refuses and touches nothing.
            await using var other = RuntimeHarness.Create(env, [], realDefinitions: true);
            var refused = await Should.ThrowAsync<Nexus.Projections.Runtime.ProjectionRebuildException>(() => other.Rebuilder.RunAsync("event_log", Ct));
            refused.Message.ShouldContain("stop nexus-projections before rebuilding event_log");
            (await env.ClickHouse.CountAsync($"SELECT count() FROM nexus_analytics.event_log FINAL WHERE {mine}")).ShouldBe(3);
        }

        // A stale row that the replay must not bring back: proves the TRUNCATE.
        var stale = Guid.CreateVersion7();
        await env.ClickHouse.ExecuteAsync(
            "INSERT INTO nexus_analytics.event_log (legal_entity_id, topic, event_id, event_type, aggregate_type, aggregate_id, aggregate_version, occurred_at, actor_user_id, correlation_id, payload_json, _ingested_at) " +
            $"VALUES (NULL, 'nexus.x.y.z', generateUUIDv4(), 'x', 'stale', '{stale}', 1, now64(6), generateUUIDv4(), generateUUIDv4(), '', now64(3))");

        // Through the CLI, as an operator would run it (the projections registered by AddNexusProjectionDefinitions).
        var exit = await ProjectionsCli.RunAsync(
            ["rebuild", "event_log"],
            s => s.AddNexusProjectionDefinitions(),
            c => c.AddInMemoryCollection(RuntimeHarness.BaseConfiguration(env)),
            Ct);

        exit.ShouldBe(ProjectionsCli.ExitOk);
        var after = await env.ClickHouse.RowsAsync($"SELECT {columns} FROM nexus_analytics.event_log FINAL WHERE {mine} ORDER BY event_id");
        after.ShouldBe(before);
        (await env.ClickHouse.CountAsync($"SELECT count() FROM nexus_analytics.event_log WHERE {mine}")).ShouldBe(3, "one row per event after the truncate");
        (await env.ClickHouse.CountAsync($"SELECT count() FROM nexus_analytics.event_log WHERE aggregate_id = '{stale}'")).ShouldBe(0);
        // Offsets handed over to the worker: committed at the end of every replayed topic.
        (await env.Kafka.CommittedAsync("projections.event_log", LegalEntityCreated)).ShouldBe(env.Kafka.HighWatermark(LegalEntityCreated));
        (await env.Kafka.CommittedAsync("projections.event_log", PartyCreated)).ShouldBe(env.Kafka.HighWatermark(PartyCreated));
    }

    private async Task CreateTopicsAsync() =>
        await env.Kafka.CreateTopicsAsync(
            LegalEntityCreated, DlqHeaders.TopicFor(LegalEntityCreated),
            "nexus.org.legal-entity.updated", "nexus.org.legal-entity.deactivated",
            PartyCreated, DlqHeaders.TopicFor(PartyCreated));

    private async Task ProduceAsync(string topic, EventEnvelope envelope, GenericRecord payload)
    {
        var bytes = await env.Kafka.SerializeAsync(topic, payload);
        await env.Kafka.ProduceRawAsync(topic, envelope.AggregateId.ToString(), bytes, KafkaFixture.HeadersFor(envelope));
    }

    private static EventEnvelope Envelope(string eventType, string aggregateType, Guid aggregateId, Guid? legalEntity, long version = 1) =>
        new(Guid.CreateVersion7(), eventType, aggregateType, aggregateId, version,
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(), legalEntity);

    private static RecordSchema OrgSchema(string file) =>
        (RecordSchema)Schema.Parse(File.ReadAllText(RepoRoot.Find($"contracts/events/org/{file}")));

    private static GenericRecord LegalEntityPayload(Guid id)
    {
        var r = new GenericRecord(OrgSchema("legal-entity.created.v1.avsc"));
        r.Add("id", id);
        r.Add("partyId", Guid.CreateVersion7());
        r.Add("groupId", Guid.CreateVersion7());
        r.Add("code", "NUS-T");
        r.Add("currencyCode", "CHF");
        r.Add("countryCode", "CH");
        r.Add("isActive", true);
        return r;
    }

    private static GenericRecord PartyPayload(Guid id)
    {
        var r = new GenericRecord(OrgSchema("party.created.v1.avsc"));
        r.Add("id", id);
        r.Add("partyType", "ORGANIZATION");
        r.Add("legalName", "Secret Legal Name AG");
        r.Add("displayName", null);
        r.Add("taxId", "CHE-123.456.789");
        r.Add("erpCompanyCode", null);
        r.Add("isInternal", false);
        r.Add("isActive", true);
        return r;
    }
}
