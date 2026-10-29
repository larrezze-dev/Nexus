using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.BusinessUnits;

/// <summary>
/// Full-state payload shared by every <c>business-unit.*</c> event (EV-R4), matching
/// <c>contracts/events/org/business-unit.*.v1.avsc</c>. <c>org</c> is GLOBAL (no RLS), so there
/// is no legal-entity scope at the envelope level even though the payload carries its own
/// <see cref="LegalEntityId"/>.
/// </summary>
public abstract record BusinessUnitEvent(
    Guid Id,
    Guid PartyId,
    Guid LegalEntityId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? CostCenterCode,
    bool IsActive) : IDomainEvent
{
    public string AggregateType => "business_unit";

    public Guid AggregateId => Id;

    public abstract string EventType { get; }

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    Guid? IDomainEvent.LegalEntityId => null;
}

/// <summary><c>nexus.org.business-unit.created</c>.</summary>
public sealed record BusinessUnitCreated(
    Guid Id, Guid PartyId, Guid LegalEntityId, string Code, IReadOnlyDictionary<string, string> CaptionI18n, string? CostCenterCode, bool IsActive)
    : BusinessUnitEvent(Id, PartyId, LegalEntityId, Code, CaptionI18n, CostCenterCode, IsActive)
{
    public override string EventType => "org.business-unit.created";
}

/// <summary><c>nexus.org.business-unit.updated</c>.</summary>
public sealed record BusinessUnitUpdated(
    Guid Id, Guid PartyId, Guid LegalEntityId, string Code, IReadOnlyDictionary<string, string> CaptionI18n, string? CostCenterCode, bool IsActive)
    : BusinessUnitEvent(Id, PartyId, LegalEntityId, Code, CaptionI18n, CostCenterCode, IsActive)
{
    public override string EventType => "org.business-unit.updated";
}

/// <summary><c>nexus.org.business-unit.deactivated</c>.</summary>
public sealed record BusinessUnitDeactivated(
    Guid Id, Guid PartyId, Guid LegalEntityId, string Code, IReadOnlyDictionary<string, string> CaptionI18n, string? CostCenterCode, bool IsActive)
    : BusinessUnitEvent(Id, PartyId, LegalEntityId, Code, CaptionI18n, CostCenterCode, IsActive)
{
    public override string EventType => "org.business-unit.deactivated";
}
