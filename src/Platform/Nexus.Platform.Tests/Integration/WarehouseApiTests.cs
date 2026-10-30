using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>/v1/warehouses</c> end to end: real host, real JWT, real PostgreSQL (all Flyway migrations
/// + <see cref="OrgFixture"/>), connecting as <c>svc_platform</c>.
/// </summary>
public sealed class WarehouseApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri Warehouses = new("/v1/warehouses", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public WarehouseApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ORG-R3: a warehouse's businessUnitId must belong to its legalEntityId")]
    public async Task Create_warehouse_with_mismatched_business_unit_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewWarehouse() with { LegalEntityId = Org.LegalEntityA.Id, BusinessUnitId = Org.BusinessUnitB.Id };

        var response = await admin.PostAsJsonAsync(Warehouses, request, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ORG-R3");
    }

    [Fact(DisplayName = "ORG-R3: a warehouse whose businessUnitId belongs to its legalEntityId is created")]
    public async Task Create_warehouse_with_matching_business_unit_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var request = NewWarehouse() with { LegalEntityId = Org.LegalEntityA.Id, BusinessUnitId = Org.BusinessUnitA.Id };

        var response = await admin.PostAsJsonAsync(Warehouses, request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        body.GetProperty("legalEntityId").GetGuid().ShouldBe(Org.LegalEntityA.Id);
        body.GetProperty("businessUnitId").GetGuid().ShouldBe(Org.BusinessUnitA.Id);
        body.GetProperty("code").GetString().ShouldBe(request.Code);
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>A valid, unique create request (own code), in LE-A/LOC-A, no business unit set.</summary>
    private CreateWarehouse NewWarehouse()
    {
        var tag = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return new CreateWarehouse(
            Id: Guid.CreateVersion7(),
            LocationId: Org.LocationA,
            LegalEntityId: Org.LegalEntityA.Id,
            BusinessUnitId: null,
            Code: "WH-TEST-" + tag,
            CaptionI18n: new Dictionary<string, string> { ["de"] = "Lager " + tag, ["en"] = "Warehouse " + tag });
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private sealed record CreateWarehouse(
        Guid? Id,
        Guid LocationId,
        Guid LegalEntityId,
        Guid? BusinessUnitId,
        string Code,
        IReadOnlyDictionary<string, string> CaptionI18n);
}
