using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests;

/// <summary>
/// The correlation middleware itself already has unit tests in
/// `Nexus.BuildingBlocks.Web.Tests` (B1); this only confirms it is actually wired into this
/// host's real pipeline (`UseNexusCorrelation()` in `Program.cs`).
/// </summary>
public sealed class CorrelationTests : IClassFixture<WebAppFixture<Program>>
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly WebAppFixture<Program> _fixture;

    public CorrelationTests(WebAppFixture<Program> fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CorrelationId_sent_by_the_caller_is_echoed_back_unchanged()
    {
        using var client = _fixture.CreateClient();
        var correlationId = Guid.CreateVersion7().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        request.Headers.Add(CorrelationHeader, correlationId);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.TryGetValues(CorrelationHeader, out var values).ShouldBeTrue();
        values!.ShouldContain(correlationId);
    }
}
