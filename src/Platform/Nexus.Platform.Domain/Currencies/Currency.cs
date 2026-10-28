using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Currencies;

/// <summary>
/// <c>org.currency</c> (domain/org.md §3). Step 0.4-B1 scaffold: table mapping only, so that
/// <c>OrgDbContext</c> covers the whole <c>org</c> schema from the start. Factory methods,
/// invariants (none so far) and events are added by the track that owns this aggregate.
/// </summary>
public sealed class Currency : AggregateRoot<string>
{
    private Currency(string id)
        : base(id)
    {
    }

    public IReadOnlyDictionary<string, string> CaptionI18n { get; private set; } = new Dictionary<string, string>();

    public short MinorUnits { get; private set; }

    public bool IsActive { get; private set; }
}
