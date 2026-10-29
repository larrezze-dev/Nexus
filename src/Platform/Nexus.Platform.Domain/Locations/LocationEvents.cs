using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Locations;

/// <summary>
/// Full-state payload shared by every <c>location.*</c> event (EV-R4), matching
/// <c>contracts/events/org/location.*.v1.avsc</c>. No <c>deactivated</c> event (domain/org.md
/// §6: Location has no deactivate operation). <c>org</c> is GLOBAL, so there is no legal-entity scope.
/// </summary>
public abstract record LocationEvent(
    Guid Id,
    string? Code,
    string Name,
    string? Street,
    string? PostalCode,
    string? City,
    string? Region,
    string CountryCode,
    bool IsActive) : IDomainEvent
{
    public string AggregateType => "location";

    public Guid AggregateId => Id;

    public abstract string EventType { get; }

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    public Guid? LegalEntityId => null;
}

/// <summary><c>nexus.org.location.created</c>.</summary>
public sealed record LocationCreated(
    Guid Id, string? Code, string Name, string? Street, string? PostalCode, string? City, string? Region, string CountryCode, bool IsActive)
    : LocationEvent(Id, Code, Name, Street, PostalCode, City, Region, CountryCode, IsActive)
{
    public override string EventType => "org.location.created";
}

/// <summary><c>nexus.org.location.updated</c>.</summary>
public sealed record LocationUpdated(
    Guid Id, string? Code, string Name, string? Street, string? PostalCode, string? City, string? Region, string CountryCode, bool IsActive)
    : LocationEvent(Id, Code, Name, Street, PostalCode, City, Region, CountryCode, IsActive)
{
    public override string EventType => "org.location.updated";
}
