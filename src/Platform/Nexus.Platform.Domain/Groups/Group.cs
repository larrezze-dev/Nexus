using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Groups;

/// <summary>
/// <c>org.&quot;group&quot;</c> (domain/org.md §3). Step 0.4-B1 scaffold: table mapping only, so that
/// <c>OrgDbContext</c> covers the whole <c>org</c> schema from the start. Factory methods,
/// invariants (ORG-R1, ORG-R7) and events are added by the track that owns this aggregate.
/// </summary>
public sealed class Group : AggregateRoot<Guid>
{
    private Group(Guid id)
        : base(id)
    {
    }

    public Guid PartyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string ConsolidationMethod { get; private set; } = string.Empty;

    public string ReportingCurrencyCode { get; private set; } = string.Empty;
}
