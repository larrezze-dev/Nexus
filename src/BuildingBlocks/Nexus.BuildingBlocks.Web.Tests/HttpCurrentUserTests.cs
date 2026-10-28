using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Authentication;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests;

public sealed class HttpCurrentUserTests
{
    private const string Oid = "6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b";
    private const string Sub = "pairwise-subject-123";

    [Fact]
    public void AuthenticatedPrincipal_ResolvesOidAndSubject_UserIdStaysNull()
    {
        var correlationId = Guid.CreateVersion7();
        var user = Create(
            new ClaimsIdentity([new Claim("oid", Oid), new Claim("sub", Sub)], authenticationType: "Bearer"),
            correlationId);

        user.IsAuthenticated.ShouldBeTrue();
        user.Oid.ShouldBe(Oid);
        user.Subject.ShouldBe(Sub);
        user.UserId.ShouldBeNull(); // resolved from oid in step 0.4, never in 0.5
        user.CorrelationId.ShouldBe(correlationId);
    }

    [Fact]
    public void AuthenticatedPrincipal_WithoutOid_HasNullOid()
    {
        var user = Create(new ClaimsIdentity([new Claim("sub", Sub)], authenticationType: "Bearer"));

        user.IsAuthenticated.ShouldBeTrue();
        user.Oid.ShouldBeNull();
        user.Subject.ShouldBe(Sub);
    }

    [Fact]
    public void MappedClaimTypes_AreIgnored_BecauseMapInboundClaimsIsOff()
    {
        // With MapInboundClaims = false the handler never produces these long URIs; reading
        // them would mean depending on a mapping that is switched off.
        var user = Create(new ClaimsIdentity(
            [
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Oid),
                new Claim(ClaimTypes.NameIdentifier, Sub),
            ],
            authenticationType: "Bearer"));

        user.Oid.ShouldBeNull();
        user.Subject.ShouldBeNull();
    }

    [Fact]
    public void UnauthenticatedPrincipal_IsNotAuthenticated_AndExposesNoIdentity()
    {
        // Claims on an identity without authentication type do not count as a validated token.
        var user = Create(new ClaimsIdentity([new Claim("oid", Oid), new Claim("sub", Sub)]));

        user.IsAuthenticated.ShouldBeFalse();
        user.Oid.ShouldBeNull();
        user.Subject.ShouldBeNull();
        user.UserId.ShouldBeNull();
    }

    [Fact]
    public void NoHttpContext_IsNotAuthenticated()
    {
        var user = new HttpCurrentUser(new HttpContextAccessor(), new CorrelationContext());

        user.IsAuthenticated.ShouldBeFalse();
        user.Oid.ShouldBeNull();
        user.Subject.ShouldBeNull();
        user.UserId.ShouldBeNull();
    }

    private static HttpCurrentUser Create(ClaimsIdentity identity, Guid correlationId = default)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new HttpCurrentUser(
            new HttpContextAccessor { HttpContext = context },
            new CorrelationContext { CorrelationId = correlationId });
    }
}
