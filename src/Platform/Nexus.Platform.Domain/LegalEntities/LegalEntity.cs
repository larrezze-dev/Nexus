using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.LegalEntities;

/// <summary>
/// <c>org.legal_entity</c> (domain/org.md §3). Step 0.4-B1 scaffold: table mapping only, so that
/// <c>OrgDbContext</c> covers the whole <c>org</c> schema from the start. Factory methods,
/// invariants (ORG-R1, ORG-R5, ORG-R7) and events are added by the track that owns this aggregate.
/// </summary>
public sealed class LegalEntity : AggregateRoot<Guid>
{
    private LegalEntity(Guid id)
        : base(id)
    {
    }

    public Guid PartyId { get; private set; }

    public Guid GroupId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string CurrencyCode { get; private set; } = string.Empty;

    public string CountryCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }
}
