using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.LegalEntities;

/// <summary>
/// Full-state payload shared by every <c>legal-entity.*</c> event (EV-R4; master data), matching
/// <c>contracts/events/org/legal-entity.*.v1.avsc</c> field by field. <c>org</c> is GLOBAL, so
/// there is no legal-entity scope on the envelope itself (<see cref="LegalEntityId"/> is always
/// <see langword="null"/>; the subject legal entity is <see cref="Id"/>, the aggregate).
/// </summary>
public abstract record LegalEntityEvent(
    Guid Id,
    Guid PartyId,
    Guid GroupId,
    string Code,
    string CurrencyCode,
    string CountryCode,
    bool IsActive) : IDomainEvent
{
    public string AggregateType => "legal-entity";

    public Guid AggregateId => Id;

    public abstract string EventType { get; }

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    public Guid? LegalEntityId => null;
}

/// <summary><c>nexus.org.legal-entity.created</c>.</summary>
public sealed record LegalEntityCreated(
    Guid Id, Guid PartyId, Guid GroupId, string Code, string CurrencyCode, string CountryCode, bool IsActive)
    : LegalEntityEvent(Id, PartyId, GroupId, Code, CurrencyCode, CountryCode, IsActive)
{
    public override string EventType => "org.legal-entity.created";
}

/// <summary><c>nexus.org.legal-entity.updated</c>.</summary>
public sealed record LegalEntityUpdated(
    Guid Id, Guid PartyId, Guid GroupId, string Code, string CurrencyCode, string CountryCode, bool IsActive)
    : LegalEntityEvent(Id, PartyId, GroupId, Code, CurrencyCode, CountryCode, IsActive)
{
    public override string EventType => "org.legal-entity.updated";
}

/// <summary><c>nexus.org.legal-entity.deactivated</c>.</summary>
public sealed record LegalEntityDeactivated(
    Guid Id, Guid PartyId, Guid GroupId, string Code, string CurrencyCode, string CountryCode, bool IsActive)
    : LegalEntityEvent(Id, PartyId, GroupId, Code, CurrencyCode, CountryCode, IsActive)
{
    public override string EventType => "org.legal-entity.deactivated";
}
