using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Warehouses;

/// <summary>
/// <c>org.warehouse</c> (domain/org.md §3). Step 0.4-B1 scaffold: table mapping only, so that
/// <c>OrgDbContext</c> covers the whole <c>org</c> schema from the start. Factory methods,
/// invariants (ORG-R3) and events are added by the track that owns this aggregate.
/// </summary>
public sealed class Warehouse : AggregateRoot<Guid>
{
    private Warehouse(Guid id)
        : base(id)
    {
    }

    public Guid LocationId { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Guid? BusinessUnitId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string> CaptionI18n { get; private set; } = new Dictionary<string, string>();

    public bool IsActive { get; private set; }
}
