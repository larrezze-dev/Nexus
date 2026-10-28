using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Identity;

namespace Nexus.BuildingBlocks.Web.Authentication;

/// <summary>
/// <see cref="ICurrentUser"/> of an HTTP request, read from the principal that the JWT bearer
/// handler validated (<see cref="NexusAuthenticationExtensions.AddNexusAuthentication"/>).
/// Registered as scoped. Values are read on access (not cached in the constructor), because
/// the instance may be resolved before the authentication middleware has run.
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICorrelationContext _correlation;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor, ICorrelationContext correlation)
    {
        _httpContextAccessor = httpContextAccessor;
        _correlation = correlation;
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string? Oid => FindClaim(JwtClaimNames.ObjectId);

    public string? Subject => FindClaim(JwtClaimNames.Subject);

    /// <summary>
    /// <c>org.app_user.id</c>, as resolved from <see cref="Oid"/> by
    /// <see cref="IdentityResolutionMiddleware"/> (step 0.4) and stored as the request's
    /// <see cref="AppUserIdentity"/> feature. <see langword="null"/> when the request is not
    /// authenticated or was not resolved (public endpoints, or before the middleware ran) — in
    /// which case no unit of work can begin (BE-R1: never an RLS session without a real user).
    /// </summary>
    public Guid? UserId => Identity?.UserId;

    /// <summary><c>org.app_user.party_id</c> of the resolved user; see <see cref="UserId"/>.</summary>
    public Guid? PartyId => Identity?.PartyId;

    /// <summary><c>org.app_user.business_unit_id</c> of the resolved user; see <see cref="UserId"/>.</summary>
    public Guid? BusinessUnitId => Identity?.BusinessUnitId;

    /// <summary>
    /// The correlation id of the request, as set by the correlation middleware
    /// (<see cref="Correlation.CorrelationMiddleware"/>); <see cref="Guid.Empty"/> if the
    /// middleware has not run. Single source of truth stays <see cref="ICorrelationContext"/>.
    /// </summary>
    public Guid CorrelationId => _correlation.CorrelationId;

    private AppUserIdentity? Identity =>
        IsAuthenticated ? _httpContextAccessor.HttpContext!.Features.Get<AppUserIdentity>() : null;

    private string? FindClaim(string type)
    {
        if (!IsAuthenticated)
        {
            return null;
        }

        var value = _httpContextAccessor.HttpContext!.User.FindFirst(type)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
