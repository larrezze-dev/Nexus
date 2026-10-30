using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Users;

/// <summary>
/// Full-state payload shared by every <c>user.*</c> event (EV-R4), matching
/// <c>contracts/events/org/user.*.v1.avsc</c>. Deliberately without <c>email</c> (EV-R5: no PII
/// in events). <c>org</c> is GLOBAL, so there is no legal-entity scope.
/// </summary>
public abstract record UserEvent(
    Guid Id,
    Guid PartyId,
    Guid? BusinessUnitId,
    string? ExternalSubject,
    string PreferredLanguage,
    bool IsActive) : IDomainEvent
{
    public string AggregateType => "user";

    public Guid AggregateId => Id;

    public abstract string EventType { get; }

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    public Guid? LegalEntityId => null;
}

/// <summary><c>nexus.org.user.created</c>.</summary>
public sealed record UserCreated(
    Guid Id, Guid PartyId, Guid? BusinessUnitId, string? ExternalSubject, string PreferredLanguage, bool IsActive)
    : UserEvent(Id, PartyId, BusinessUnitId, ExternalSubject, PreferredLanguage, IsActive)
{
    public override string EventType => "org.user.created";
}

/// <summary><c>nexus.org.user.updated</c>.</summary>
public sealed record UserUpdated(
    Guid Id, Guid PartyId, Guid? BusinessUnitId, string? ExternalSubject, string PreferredLanguage, bool IsActive)
    : UserEvent(Id, PartyId, BusinessUnitId, ExternalSubject, PreferredLanguage, IsActive)
{
    public override string EventType => "org.user.updated";
}

/// <summary><c>nexus.org.user.deactivated</c>.</summary>
public sealed record UserDeactivated(
    Guid Id, Guid PartyId, Guid? BusinessUnitId, string? ExternalSubject, string PreferredLanguage, bool IsActive)
    : UserEvent(Id, PartyId, BusinessUnitId, ExternalSubject, PreferredLanguage, IsActive)
{
    public override string EventType => "org.user.deactivated";
}
