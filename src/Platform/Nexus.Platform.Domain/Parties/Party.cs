using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Parties;

/// <summary>
/// The universal directory entry (<c>org.party</c>, domain/org.md §2): every person or
/// organization NEXUS knows about, internal or external.
/// <para>
/// Step 0.4-B1 only implements what creating and editing an <see cref="Users.AppUser"/> needs
/// (its internal <c>PERSON</c> party is created and renamed in the same use case). The rest of
/// the Party API (<c>/v1/parties</c>: generic create/update/deactivate, ORG-R1 for structure
/// entities) is added on top of this class by the Party track.
/// </para>
/// </summary>
public sealed class Party : AggregateRoot<Guid>
{
    private Party(Guid id)
        : base(id)
    {
    }

    /// <summary>Fixed at creation: there is deliberately no way to change it (Rule ORG-R2).</summary>
    public PartyType PartyType { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public string? DisplayName { get; private set; }

    public string? TaxId { get; private set; }

    public string? ErpCompanyCode { get; private set; }

    /// <summary><see langword="true"/> = an entity of the group (domain/org.md §3).</summary>
    public bool IsInternal { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Creates an active party and raises <see cref="PartyCreated"/>. Shape (non-empty names,
    /// lengths) is validated by the calling command's validator; here it is a guard.
    /// </summary>
    public static Result<Party> Create(
        Guid id,
        PartyType partyType,
        string legalName,
        string? displayName,
        string? taxId,
        string? erpCompanyCode,
        bool isInternal)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A party id must not be empty.", nameof(id));
        }

        Guard.Against.NullOrWhiteSpace(legalName, nameof(legalName));

        var party = new Party(id)
        {
            PartyType = partyType,
            LegalName = legalName,
            DisplayName = displayName,
            TaxId = taxId,
            ErpCompanyCode = erpCompanyCode,
            IsInternal = isInternal,
            IsActive = true,
        };

        party.RaiseEvent(new PartyCreated(
            party.Id, party.PartyType.ToName(), party.LegalName, party.DisplayName, party.TaxId,
            party.ErpCompanyCode, party.IsInternal, party.IsActive));

        return Result.Success(party);
    }

    /// <summary>
    /// Changes <see cref="LegalName"/>. Returns <see langword="true"/> and raises
    /// <see cref="PartyUpdated"/> only if it actually changed (no event for a no-op).
    /// </summary>
    public bool Rename(string legalName)
    {
        Guard.Against.NullOrWhiteSpace(legalName, nameof(legalName));

        if (string.Equals(LegalName, legalName, StringComparison.Ordinal))
        {
            return false;
        }

        LegalName = legalName;
        RaiseUpdated();
        return true;
    }

    private void RaiseUpdated() =>
        RaiseEvent(new PartyUpdated(
            Id, PartyType.ToName(), LegalName, DisplayName, TaxId, ErpCompanyCode, IsInternal, IsActive));
}
