using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.BusinessUnits;

/// <summary>
/// <c>org.business_unit</c> (domain/org.md §3). Step 0.4-B1 scaffold: table mapping only, so that
/// <c>OrgDbContext</c> covers the whole <c>org</c> schema from the start. Factory methods,
/// invariants (ORG-R1) and events are added by the track that owns this aggregate.
/// </summary>
public sealed class BusinessUnit : AggregateRoot<Guid>
{
    private BusinessUnit(Guid id)
        : base(id)
    {
    }

    public Guid PartyId { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string> CaptionI18n { get; private set; } = new Dictionary<string, string>();

    public string? CostCenterCode { get; private set; }

    public bool IsActive { get; private set; }
}
