using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Nexus.BuildingBlocks.Testing;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>Nexus.Platform.Api</c> in memory (<see cref="WebAppFixture{TProgram}"/>: JWTs from
/// <see cref="WebAppFixture{TProgram}.JwtIssuer"/>) on top of a real PostgreSQL with every
/// Flyway migration applied and the <see cref="OrgFixture"/> data seeded. The host connects as
/// <c>svc_platform</c>, exactly like the deployed service.
/// <para>
/// One instance per test class (class fixture): a clean database per class
/// (conventions-testing.md §4). Tests inside a class create their own users with unique
/// emails/oids, so they never depend on each other.
/// </para>
/// </summary>
public sealed class PlatformApiFixture : WebAppFixture<Program>, IAsyncLifetime
{
    public OrgFixture Org { get; } = new();

    public PostgresFixture Database => Org.Database;

    public ValueTask InitializeAsync() => Org.InitializeAsync();

    /// <summary>A client whose requests carry a valid JWT with <paramref name="oid"/>.</summary>
    public HttpClient CreateClientFor(string oid)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuer.IssueToken(oid: oid));
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Org.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Nexus:Database:ConnectionString", Database.GetConnectionString("svc_platform"));
    }
}
