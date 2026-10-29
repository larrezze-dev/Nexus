using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Warehouses;

/// <summary>
/// Full-state payload shared by every <c>warehouse.*</c> event (EV-R4), matching
/// <c>contracts/events/org/warehouse.*.v1.avsc</c>. <c>org</c> is GLOBAL (no RLS), so there is no
/// legal-entity scope at the envelope level even though the payload carries its own
/// <see cref="LegalEntityId"/>.
/// </summary>
public abstract record WarehouseEvent(
    Guid Id,
    Guid LocationId,
    Guid LegalEntityId,
    Guid? BusinessUnitId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    bool IsActive) : IDomainEvent
{
    public string AggregateType => "warehouse";

    public Guid AggregateId => Id;

    public abstract string EventType { get; }

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    Guid? IDomainEvent.LegalEntityId => null;
}

/// <summary><c>nexus.org.warehouse.created</c>.</summary>
public sealed record WarehouseCreated(
    Guid Id, Guid LocationId, Guid LegalEntityId, Guid? BusinessUnitId, string Code, IReadOnlyDictionary<string, string> CaptionI18n, bool IsActive)
    : WarehouseEvent(Id, LocationId, LegalEntityId, BusinessUnitId, Code, CaptionI18n, IsActive)
{
    public override string EventType => "org.warehouse.created";
}

/// <summary><c>nexus.org.warehouse.updated</c>.</summary>
public sealed record WarehouseUpdated(
    Guid Id, Guid LocationId, Guid LegalEntityId, Guid? BusinessUnitId, string Code, IReadOnlyDictionary<string, string> CaptionI18n, bool IsActive)
    : WarehouseEvent(Id, LocationId, LegalEntityId, BusinessUnitId, Code, CaptionI18n, IsActive)
{
    public override string EventType => "org.warehouse.updated";
}

/// <summary><c>nexus.org.warehouse.deactivated</c>.</summary>
public sealed record WarehouseDeactivated(
    Guid Id, Guid LocationId, Guid LegalEntityId, Guid? BusinessUnitId, string Code, IReadOnlyDictionary<string, string> CaptionI18n, bool IsActive)
    : WarehouseEvent(Id, LocationId, LegalEntityId, BusinessUnitId, Code, CaptionI18n, IsActive)
{
    public override string EventType => "org.warehouse.deactivated";
}
