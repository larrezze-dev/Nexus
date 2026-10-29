using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Warehouses;

/// <summary>
/// <c>org.warehouse</c> (domain/org.md §3): a physical storage location
/// (<see cref="LocationId"/>) owned by a <c>LegalEntity</c> (<see cref="LegalEntityId"/>) and
/// optionally assigned to one of its <c>BusinessUnit</c>s (<see cref="BusinessUnitId"/>, Rule
/// ORG-R3).
/// <para>
/// ORG-R3 ("if <c>BusinessUnitId</c> is set, that business unit must belong to the same
/// <c>LegalEntityId</c>") is enforced by the create/update <b>handlers</b>
/// (<c>Nexus.Platform.Application.Warehouses</c>), not here: checking it needs the referenced
/// <c>BusinessUnit</c>'s own <c>legal_entity_id</c>, a fact only the database can provide, and
/// this aggregate must stay free of persistence concerns (conventions-backend.md §3). The
/// aggregate only stores the already-validated ids.
/// </para>
/// </summary>
public sealed class Warehouse : AggregateRoot<Guid>
{
    private Warehouse(Guid id)
        : base(id)
    {
    }

    public Guid LocationId { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Guid? BusinessUnitId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string> CaptionI18n { get; private set; } = new Dictionary<string, string>();

    public bool IsActive { get; private set; }

    /// <summary>Creates an active warehouse and raises <see cref="WarehouseCreated"/>. ORG-R3 is already checked by the caller.</summary>
    public static Result<Warehouse> Create(
        Guid id,
        Guid locationId,
        Guid legalEntityId,
        Guid? businessUnitId,
        string code,
        IReadOnlyDictionary<string, string> captionI18n)
    {
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.Null(captionI18n, nameof(captionI18n));

        var warehouse = new Warehouse(id)
        {
            LocationId = locationId,
            LegalEntityId = legalEntityId,
            BusinessUnitId = businessUnitId,
            Code = code,
            CaptionI18n = captionI18n,
            IsActive = true,
        };

        warehouse.RaiseEvent(new WarehouseCreated(
            warehouse.Id, warehouse.LocationId, warehouse.LegalEntityId, warehouse.BusinessUnitId,
            warehouse.Code, warehouse.CaptionI18n, warehouse.IsActive));

        return Result.Success(warehouse);
    }

    /// <summary>
    /// Replaces <see cref="LocationId"/>, <see cref="BusinessUnitId"/>, <see cref="Code"/> and
    /// <see cref="CaptionI18n"/> (PUT semantics). <see cref="LegalEntityId"/> never changes
    /// after creation (not part of <c>UpdateWarehouseRequest</c>). ORG-R3 is already checked by
    /// the caller when <paramref name="businessUnitId"/> is set. Returns whether anything
    /// changed; raises <see cref="WarehouseUpdated"/> only in that case.
    /// </summary>
    public bool Update(Guid locationId, Guid? businessUnitId, string code, IReadOnlyDictionary<string, string> captionI18n)
    {
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.Null(captionI18n, nameof(captionI18n));

        var changed =
            LocationId != locationId
            || BusinessUnitId != businessUnitId
            || !string.Equals(Code, code, StringComparison.Ordinal)
            || !CaptionEquals(captionI18n);

        if (!changed)
        {
            return false;
        }

        LocationId = locationId;
        BusinessUnitId = businessUnitId;
        Code = code;
        CaptionI18n = captionI18n;

        RaiseEvent(new WarehouseUpdated(Id, LocationId, LegalEntityId, BusinessUnitId, Code, CaptionI18n, IsActive));
        return true;
    }

    /// <summary>
    /// Deactivates the warehouse (no DELETE in master data). Idempotent: deactivating an
    /// already inactive warehouse changes nothing and raises nothing. Returns whether anything changed.
    /// </summary>
    public bool Deactivate()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        RaiseEvent(new WarehouseDeactivated(Id, LocationId, LegalEntityId, BusinessUnitId, Code, CaptionI18n, IsActive));
        return true;
    }

    private bool CaptionEquals(IReadOnlyDictionary<string, string> other) =>
        CaptionI18n.Count == other.Count
        && CaptionI18n.All(pair => other.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
}
