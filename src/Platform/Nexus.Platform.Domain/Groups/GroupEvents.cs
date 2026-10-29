using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Groups;

/// <summary>
/// Full-state payload shared by every <c>group.*</c> event (EV-R4; master data), matching
/// <c>contracts/events/org/group.*.v1.avsc</c> field by field. <c>org</c> is GLOBAL, so there is
/// no legal-entity scope.
/// </summary>
public abstract record GroupEvent(
    Guid Id,
    Guid PartyId,
    string Code,
    string ConsolidationMethod,
    string ReportingCurrencyCode) : IDomainEvent
{
    public string AggregateType => "group";

    public Guid AggregateId => Id;

    public abstract string EventType { get; }

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    public Guid? LegalEntityId => null;
}

/// <summary><c>nexus.org.group.created</c>.</summary>
public sealed record GroupCreated(Guid Id, Guid PartyId, string Code, string ConsolidationMethod, string ReportingCurrencyCode)
    : GroupEvent(Id, PartyId, Code, ConsolidationMethod, ReportingCurrencyCode)
{
    public override string EventType => "org.group.created";
}

/// <summary><c>nexus.org.group.updated</c>.</summary>
public sealed record GroupUpdated(Guid Id, Guid PartyId, string Code, string ConsolidationMethod, string ReportingCurrencyCode)
    : GroupEvent(Id, PartyId, Code, ConsolidationMethod, ReportingCurrencyCode)
{
    public override string EventType => "org.group.updated";
}
