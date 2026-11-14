using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// Group names (UI contract 2): <c>legalName</c>/<c>displayName</c> come from the group's
/// <c>org.party</c> in listGroups, getGroup, createGroup and updateGroup (ORG-R1).
/// </summary>
public sealed class GroupApiTests : IClassFixture<PlatformApiFixture>
{
    private readonly PlatformApiFixture _fixture;

    public GroupApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ORG-R1: create, get, update and list groups return the party's legalName/displayName")]
    public async Task Groups_expose_party_names_everywhere()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var create = await admin.PostAsJsonAsync(
            new Uri("/v1/groups", UriKind.Relative),
            new { legalName = "Holding " + tag + " AG", displayName = "Holding " + tag, code = "G-" + tag, consolidationMethod = "FULL", reportingCurrencyCode = "CHF" },
            Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadAsync(create);
        created.GetProperty("legalName").GetString().ShouldBe("Holding " + tag + " AG");
        created.GetProperty("displayName").GetString().ShouldBe("Holding " + tag);
        var id = created.GetProperty("id").GetGuid();

        var get = await ReadAsync(await admin.GetAsync(new Uri($"/v1/groups/{id}", UriKind.Relative), Ct));
        get.GetProperty("legalName").GetString().ShouldBe("Holding " + tag + " AG");
        get.GetProperty("displayName").GetString().ShouldBe("Holding " + tag);

        using var put = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/groups/{id}", UriKind.Relative))
        {
            Content = JsonContent.Create(new { code = "G-" + tag, consolidationMethod = "PROPORTIONAL", reportingCurrencyCode = "CHF" }),
        };
        put.Headers.TryAddWithoutValidation("If-Match", create.Headers.ETag!.Tag);
        var update = await admin.SendAsync(put, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ReadAsync(update);
        updated.GetProperty("consolidationMethod").GetString().ShouldBe("PROPORTIONAL");
        updated.GetProperty("legalName").GetString().ShouldBe("Holding " + tag + " AG");
        updated.GetProperty("displayName").GetString().ShouldBe("Holding " + tag);

        var list = await ReadAsync(await admin.GetAsync(new Uri("/v1/groups?pageSize=200", UriKind.Relative), Ct));
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe((int)await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM org.\"group\""));
        var item = items.Single(i => i.GetProperty("id").GetGuid() == id);
        item.GetProperty("legalName").GetString().ShouldBe("Holding " + tag + " AG");
        item.GetProperty("displayName").GetString().ShouldBe("Holding " + tag);

        // The fixture's own group has no display name: null, not missing/empty.
        var seeded = items.Single(i => i.GetProperty("id").GetGuid() == Org.GroupId);
        seeded.GetProperty("legalName").GetString().ShouldNotBeNullOrWhiteSpace();
        seeded.GetProperty("displayName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;
}
