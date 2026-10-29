using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Parties;

/// <summary>
/// Full-state payload shared by every <c>party.*</c> event (EV-R4; master data). The property
/// names match <c>contracts/events/org/party.*.v1.avsc</c> field by field (EV-R2: the outbox
/// validates them). <c>org</c> is GLOBAL, so there is no legal-entity scope.
/// </summary>
public abstract record PartyEvent(
    Guid Id,
    string PartyType,
    string LegalName,
    string? DisplayName,
    string? TaxId,
    string? ErpCompanyCode,
    bool IsInternal,
    bool IsActive) : IDomainEvent
{
    public string AggregateType => "party";

    public Guid AggregateId => Id;

    public abstract string EventType { get; }

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    public Guid? LegalEntityId => null;
}

/// <summary><c>nexus.org.party.created</c>.</summary>
public sealed record PartyCreated(
    Guid Id, string PartyType, string LegalName, string? DisplayName, string? TaxId, string? ErpCompanyCode, bool IsInternal, bool IsActive)
    : PartyEvent(Id, PartyType, LegalName, DisplayName, TaxId, ErpCompanyCode, IsInternal, IsActive)
{
    public override string EventType => "org.party.created";
}

/// <summary><c>nexus.org.party.updated</c>.</summary>
public sealed record PartyUpdated(
    Guid Id, string PartyType, string LegalName, string? DisplayName, string? TaxId, string? ErpCompanyCode, bool IsInternal, bool IsActive)
    : PartyEvent(Id, PartyType, LegalName, DisplayName, TaxId, ErpCompanyCode, IsInternal, IsActive)
{
    public override string EventType => "org.party.updated";
}

/// <summary><c>nexus.org.party.deactivated</c>.</summary>
public sealed record PartyDeactivated(
    Guid Id, string PartyType, string LegalName, string? DisplayName, string? TaxId, string? ErpCompanyCode, bool IsInternal, bool IsActive)
    : PartyEvent(Id, PartyType, LegalName, DisplayName, TaxId, ErpCompanyCode, IsInternal, IsActive)
{
    public override string EventType => "org.party.deactivated";
}
