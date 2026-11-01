namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// The four scope arguments of <c>access.has_permission(p_permission, p_legal_entity_id,
/// p_business_unit_id, p_warehouse_id, p_project_id)</c> (domain/access.md §4).
/// </summary>
internal readonly record struct PermissionTarget(
    Guid? LegalEntityId,
    Guid? BusinessUnitId,
    Guid? WarehouseId,
    Guid? ProjectId)
{
    /// <summary>All four <c>NULL</c>: "does the caller hold the permission in any scope?"
    /// (the listing-only form, ACC-R10).</summary>
    public static PermissionTarget AnyScope => default;

    /// <summary>
    /// A target that only a <c>GLOBAL</c>-scoped assignment can satisfy.
    /// <para>
    /// Why a fresh random id: <c>has_permission</c> treats "every scope argument is NULL" as
    /// "any scope" (its catch-all clause), so NULLs cannot express "GLOBAL only". A non-NULL
    /// id that does not exist anywhere defeats that catch-all; the derived group
    /// (<c>org.legal_entity</c> lookup) and business unit (<c>org.warehouse</c> lookup) come
    /// out NULL or equal to the same random id, so the GROUP/LEGAL_ENTITY/BUSINESS_UNIT/
    /// WAREHOUSE/PROJECT clauses can only match an assignment whose <c>scope_entity_id</c>
    /// literally equals this value, and no real row will ever hold a freshly generated random
    /// GUID. That leaves <c>scope_type = 'GLOBAL'</c> as the only clause that can match.
    /// </para>
    /// <para>
    /// <see cref="Guid.NewGuid"/> (random v4), not UUIDv7: this value is never stored, and full
    /// 122-bit randomness is exactly what makes the collision argument above hold. A fresh one
    /// per call, never a well-known constant that someone could one day insert as a row id.
    /// </para>
    /// </summary>
    public static PermissionTarget GlobalOnly()
    {
        var unreachable = Guid.NewGuid();
        return new PermissionTarget(unreachable, unreachable, unreachable, unreachable);
    }
}
