using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;

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
    /// Always <see langword="null"/> in step 0.5: resolving <see cref="Oid"/> to
    /// <c>org.app_user.id</c> (and the 403 <c>user_not_provisioned</c>) arrives with the
    /// organisational core in step 0.4. Until then no HTTP request can open a unit of work,
    /// which is intended (BE-R1: never an RLS session without a real user).
    /// </summary>
    public Guid? UserId => null;

    /// <summary>
    /// The correlation id of the request, as set by the correlation middleware
    /// (<see cref="Correlation.CorrelationMiddleware"/>); <see cref="Guid.Empty"/> if the
    /// middleware has not run. Single source of truth stays <see cref="ICorrelationContext"/>.
    /// </summary>
    public Guid CorrelationId => _correlation.CorrelationId;

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
