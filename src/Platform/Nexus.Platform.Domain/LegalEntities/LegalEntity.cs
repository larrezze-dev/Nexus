using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Domain.LegalEntities;

/// <summary>
/// <c>org.legal_entity</c> (domain/org.md §3): a legal entity of the group, in exactly one
/// <see cref="GroupId"/>.
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

    /// <summary>
    /// Creates an active legal entity for <paramref name="party"/> and raises
    /// <see cref="LegalEntityCreated"/>. The currency's existence/activity (ORG-R7) is checked
    /// by the calling command before this is called (no database access from the aggregate);
    /// here only ORG-R1 is enforced.
    /// </summary>
    public static Result<LegalEntity> Create(
        Guid id,
        Party party,
        Guid groupId,
        string code,
        string currencyCode,
        string countryCode)
    {
        Guard.Against.Null(party, nameof(party));
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.NullOrWhiteSpace(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrWhiteSpace(countryCode, nameof(countryCode));
        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A legal entity's groupId must not be empty.", nameof(groupId));
        }

        var org1 = EnsureInternalOrganizationParty(party);
        if (org1.IsFailure)
        {
            return Result.Failure<LegalEntity>(org1.Error!);
        }

        var legalEntity = new LegalEntity(id)
        {
            PartyId = party.Id,
            GroupId = groupId,
            Code = code,
            CurrencyCode = currencyCode,
            CountryCode = countryCode,
            IsActive = true,
        };

        legalEntity.RaiseEvent(new LegalEntityCreated(
            legalEntity.Id, legalEntity.PartyId, legalEntity.GroupId, legalEntity.Code,
            legalEntity.CurrencyCode, legalEntity.CountryCode, legalEntity.IsActive));

        return Result.Success(legalEntity);
    }

    /// <summary>
    /// Replaces <see cref="Code"/>, <see cref="CurrencyCode"/> and <see cref="CountryCode"/>
    /// (PUT semantics; the contract's <c>UpdateLegalEntityRequest</c> has no <c>groupId</c>).
    /// The new currency's activity (ORG-R7) is checked by the calling command before this is
    /// called. Returns whether anything changed; raises <see cref="LegalEntityUpdated"/> only in
    /// that case.
    /// </summary>
    public Result<bool> Update(string code, string currencyCode, string countryCode)
    {
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.NullOrWhiteSpace(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrWhiteSpace(countryCode, nameof(countryCode));

        var changed =
            !string.Equals(Code, code, StringComparison.Ordinal)
            || !string.Equals(CurrencyCode, currencyCode, StringComparison.Ordinal)
            || !string.Equals(CountryCode, countryCode, StringComparison.Ordinal);

        if (!changed)
        {
            return Result.Success(false);
        }

        Code = code;
        CurrencyCode = currencyCode;
        CountryCode = countryCode;

        RaiseEvent(new LegalEntityUpdated(Id, PartyId, GroupId, Code, CurrencyCode, CountryCode, IsActive));
        return Result.Success(true);
    }

    /// <summary>
    /// Deactivates the legal entity (no DELETE in master data). Whether it has an active
    /// <c>BusinessUnit</c>/<c>Warehouse</c> (ORG-R5) is checked by the calling command before
    /// this is called (no database access from the aggregate). Idempotent: deactivating an
    /// already inactive legal entity changes nothing and raises nothing.
    /// </summary>
    public Result<bool> Deactivate()
    {
        if (!IsActive)
        {
            return Result.Success(false);
        }

        IsActive = false;
        RaiseEvent(new LegalEntityDeactivated(Id, PartyId, GroupId, Code, CurrencyCode, CountryCode, IsActive));
        return Result.Success(true);
    }

    /// <summary>Rule ORG-R1: <paramref name="party"/> must be an internal <c>ORGANIZATION</c>.</summary>
    internal static Result EnsureInternalOrganizationParty(Party party) =>
        party.PartyType != PartyType.Organization || !party.IsInternal
            ? Result.Failure(new Error("ORG-R1", "A LegalEntity's party must be an internal ORGANIZATION."))
            : Result.Success();
}
