using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// EV-R4 (conventions-events.md §120): a master-data event's outbox payload carries the full
/// state of the aggregate after the change, not just its id and a bare fact. Real host, real
/// JWT, real PostgreSQL (all Flyway migrations + <see cref="OrgFixture"/>); the payload is read
/// straight out of <c>outbox.message</c> (Avro JSON encoding, conventions-events.md §6) as the
/// superuser, since business roles only hold INSERT on that table.
/// </summary>
public sealed class OutboxEventPayloadTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri BusinessUnits = new("/v1/business-units", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public OutboxEventPayloadTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "EV-R4: a master-data event's outbox payload carries the aggregate's full state, not just its id")]
    public async Task Create_business_unit_writes_full_state_outbox_payload()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var id = Guid.CreateVersion7();
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var code = "BU-TEST-" + tag;
        var costCenterCode = "CC-" + tag;
        var request = new
        {
            Id = id,
            LegalName = "Test business unit " + tag,
            LegalEntityId = Org.LegalEntityA.Id,
            Code = code,
            CaptionI18n = new Dictionary<string, string> { ["de"] = "Geschäftseinheit " + tag, ["en"] = "Business unit " + tag },
            CostCenterCode = costCenterCode,
        };

        var response = await admin.PostAsJsonAsync(BusinessUnits, request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;
        var legalEntityId = created.GetProperty("legalEntityId").GetGuid();
        legalEntityId.ShouldBe(Org.LegalEntityA.Id);

        var payload = await _fixture.Database.ScalarAsync<string>(
            "SELECT payload::text FROM outbox.message WHERE aggregate_id = $1 AND event_type = 'org.business-unit.created'", id);
        payload.ShouldNotBeNullOrEmpty();

        // Scalar fields: plain values in Avro JSON encoding (non-union / non-decimal fields are
        // encoded like ordinary JSON scalars).
        payload.ShouldContain($"\"id\":\"{id}\"");
        payload.ShouldContain($"\"legalEntityId\":\"{Org.LegalEntityA.Id}\"");
        payload.ShouldContain($"\"code\":\"{code}\"");
        payload.ShouldContain("\"isActive\":true");

        // "costCenterCode" is an optional union (["null", "string"]): a non-null value encodes
        // as {"string": "..."} , not a bare string (conventions-events.md §6, Avro JSON union
        // encoding) -- confirmed by reading AvroPayloadSerializer's union handling.
        payload.ShouldContain($"\"costCenterCode\":{{\"string\":\"{costCenterCode}\"}}");

        // partyId travels too (full post-create state, not just the business unit's own id): it
        // is a plain uuid field, so it is some other GUID than the business unit's own id.
        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;
        root.TryGetProperty("partyId", out var partyIdElement).ShouldBeTrue();
        partyIdElement.GetString().ShouldNotBeNullOrEmpty();
        partyIdElement.GetString().ShouldNotBe(id.ToString());

        // captionI18n is a map: structurally present with the "de" entry posted above, not just
        // a non-empty string.
        root.TryGetProperty("captionI18n", out var captionElement).ShouldBeTrue();
        captionElement.ValueKind.ShouldBe(JsonValueKind.Object);
        captionElement.GetProperty("de").GetString().ShouldBe(request.CaptionI18n["de"]);
    }
}
