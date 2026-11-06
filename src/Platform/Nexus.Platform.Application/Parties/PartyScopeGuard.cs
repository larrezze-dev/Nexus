using Nexus.BuildingBlocks.Application;

namespace Nexus.Platform.Application.Parties;

/// <summary>
/// Scope check of the party write endpoints (<c>updateParty</c>, <c>deactivateParty</c>, both
/// <c>PARTY:UPDATE</c>, <c>x-scope: none</c>). A legal entity, a business unit and a group each
/// have their own internal party (ORG-R1) holding their <c>legalName</c>/<c>taxId</c>/
/// <c>erpCompanyCode</c>: writing that party is writing the org entity, so it needs the same
/// scope coverage the entity's own endpoints check (ACC-R10). A party that backs none of them
/// (an external/directory party, or a user's <c>PERSON</c> party) has no single clean scope
/// and stays unscoped, as before.
/// </summary>
internal static class PartyScopeGuard
{
    /// <summary>
    /// <see langword="true"/> if <paramref name="partyId"/> backs no org entity, or the caller's
    /// <paramref name="permission"/> covers <b>every</b> org entity it backs
    /// (<c>LEGAL_ENTITY</c>/<c>BUSINESS_UNIT</c>/<c>GROUP</c> via
    /// <see cref="IRoleAssignmentScopeChecker"/>, the same hierarchy the entity endpoints use).
    /// </summary>
    public static async Task<bool> CoversBackedOrgEntitiesAsync(
        IPartyReadDb readDb,
        IRoleAssignmentScopeChecker scopeChecker,
        string permission,
        Guid partyId,
        CancellationToken cancellationToken)
    {
        var backed = await readDb.FindBackedOrgEntitiesAsync(partyId, cancellationToken);
        foreach (var entity in backed)
        {
            if (!await scopeChecker.HasPermissionCoveringAsync(permission, entity.ScopeType, entity.EntityId, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }
}
