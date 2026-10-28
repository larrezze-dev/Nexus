namespace Nexus.BuildingBlocks.Web.Identity;

/// <summary>
/// The NEXUS identity of an authenticated caller: the active <c>org.app_user</c> row its
/// <c>oid</c> claim maps to (ADR-0006). Produced once per request by
/// <see cref="IdentityResolutionMiddleware"/> and stored as an HTTP feature
/// (<c>HttpContext.Features.Get&lt;AppUserIdentity&gt;()</c>), from where
/// <see cref="Authentication.HttpCurrentUser"/> exposes it as <c>ICurrentUser.UserId</c>/
/// <c>PartyId</c>/<c>BusinessUnitId</c>.
/// <para>
/// A request feature rather than a scoped service on purpose: the identity belongs to the
/// request, so any DI scope created while serving it (e.g. a background-style scope opened by
/// an endpoint) sees the same identity instead of an empty copy.
/// </para>
/// </summary>
/// <param name="UserId"><c>org.app_user.id</c>.</param>
/// <param name="PartyId"><c>org.app_user.party_id</c> (a <c>PERSON</c> party, ORG-R4).</param>
/// <param name="BusinessUnitId"><c>org.app_user.business_unit_id</c>; optional.</param>
public sealed record AppUserIdentity(Guid UserId, Guid PartyId, Guid? BusinessUnitId);
