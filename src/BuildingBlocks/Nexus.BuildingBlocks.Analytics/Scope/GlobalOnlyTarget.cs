namespace Nexus.BuildingBlocks.Analytics.Scope;

/// <summary>
/// The "only a <c>GLOBAL</c> assignment can satisfy this" target of
/// <c>access.has_permission</c>. Same technique, same reasoning as
/// <c>Nexus.BuildingBlocks.Web.Authorization.PermissionTarget.GlobalOnly()</c> (step 0.6), which
/// is the reference implementation; this assembly must not depend on the web building block, so
/// it keeps its own copy of the one line that matters. Both must stay equivalent; the tests of
/// this project pin the behaviour (a GLOBAL holder passes, LEGAL_ENTITY/GROUP/BU/WAREHOUSE holders
/// do not).
/// <para>
/// Why a fresh random id: <c>has_permission</c> treats "every scope argument is NULL" as "any
/// scope" (its catch-all clause), so NULLs cannot express "GLOBAL only". Passing the same freshly
/// generated id as legal entity, business unit, warehouse and project defeats the catch-all; the
/// derived group (<c>org.legal_entity</c> lookup) comes out NULL and the derived business unit
/// equals the same random id, so the GROUP/LEGAL_ENTITY/BUSINESS_UNIT/WAREHOUSE/PROJECT clauses
/// can only match an assignment whose <c>scope_entity_id</c> literally equals it, which no real
/// row will ever hold. <c>p_group_id</c> stays NULL. That leaves <c>scope_type = 'GLOBAL'</c>.
/// </para>
/// <para>
/// <see cref="Guid.NewGuid"/> (random v4, 122 random bits), never UUIDv7 and never a constant:
/// the value is not stored anywhere, and full randomness per call is what makes the collision
/// argument hold.
/// </para>
/// </summary>
internal static class GlobalOnlyTarget
{
    /// <summary>A fresh id that no organisational row or assignment can hold.</summary>
    public static Guid NewUnreachableId() => Guid.NewGuid();
}
