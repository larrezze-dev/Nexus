using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Locations;

/// <summary>
/// <c>org.location</c> (domain/org.md §3). Step 0.4-B1 scaffold: table mapping only, so that
/// <c>OrgDbContext</c> covers the whole <c>org</c> schema from the start. Factory methods,
/// invariants (none so far) and events are added by the track that owns this aggregate.
/// </summary>
public sealed class Location : AggregateRoot<Guid>
{
    private Location(Guid id)
        : base(id)
    {
    }

    public string? Code { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Street { get; private set; }

    public string? PostalCode { get; private set; }

    public string? City { get; private set; }

    public string? Region { get; private set; }

    public string CountryCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }
}
