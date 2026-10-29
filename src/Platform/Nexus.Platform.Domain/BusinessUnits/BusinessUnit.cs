using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Domain.BusinessUnits;

/// <summary>
/// <c>org.business_unit</c> (domain/org.md §3): an operating unit of a <c>LegalEntity</c>
/// (<see cref="LegalEntityId"/>), itself an internal <see cref="Parties.Party"/>
/// (<see cref="PartyId"/>, Rule ORG-R1).
/// </summary>
public sealed class BusinessUnit : AggregateRoot<Guid>
{
    private static readonly Error NotAnInternalOrganization =
        new("ORG-R1", "A business unit's party must be an internal ORGANIZATION.");

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

    /// <summary>
    /// Creates an active business unit for <paramref name="party"/> (its internal
    /// <c>ORGANIZATION</c> party, created inline by the caller: ORG-R1) and raises
    /// <see cref="BusinessUnitCreated"/>.
    /// </summary>
    public static Result<BusinessUnit> Create(
        Guid id,
        Party party,
        Guid legalEntityId,
        string code,
        IReadOnlyDictionary<string, string> captionI18n,
        string? costCenterCode)
    {
        Guard.Against.Null(party, nameof(party));
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.Null(captionI18n, nameof(captionI18n));

        // Rule ORG-R1: Group, LegalEntity and BusinessUnit point to an internal ORGANIZATION party.
        if (party.PartyType != PartyType.Organization || !party.IsInternal)
        {
            return Result.Failure<BusinessUnit>(NotAnInternalOrganization);
        }

        var businessUnit = new BusinessUnit(id)
        {
            PartyId = party.Id,
            LegalEntityId = legalEntityId,
            Code = code,
            CaptionI18n = captionI18n,
            CostCenterCode = costCenterCode,
            IsActive = true,
        };

        businessUnit.RaiseEvent(new BusinessUnitCreated(
            businessUnit.Id, businessUnit.PartyId, businessUnit.LegalEntityId, businessUnit.Code,
            businessUnit.CaptionI18n, businessUnit.CostCenterCode, businessUnit.IsActive));

        return Result.Success(businessUnit);
    }

    /// <summary>
    /// Replaces <see cref="Code"/>, <see cref="CaptionI18n"/> and <see cref="CostCenterCode"/>
    /// (PUT semantics). <see cref="LegalEntityId"/> never changes after creation (not part of
    /// <c>UpdateBusinessUnitRequest</c>). Returns whether anything changed; raises
    /// <see cref="BusinessUnitUpdated"/> only in that case.
    /// </summary>
    public bool Update(string code, IReadOnlyDictionary<string, string> captionI18n, string? costCenterCode)
    {
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.Null(captionI18n, nameof(captionI18n));

        var changed =
            !string.Equals(Code, code, StringComparison.Ordinal)
            || !CaptionEquals(captionI18n)
            || !string.Equals(CostCenterCode, costCenterCode, StringComparison.Ordinal);

        if (!changed)
        {
            return false;
        }

        Code = code;
        CaptionI18n = captionI18n;
        CostCenterCode = costCenterCode;

        RaiseEvent(new BusinessUnitUpdated(Id, PartyId, LegalEntityId, Code, CaptionI18n, CostCenterCode, IsActive));
        return true;
    }

    /// <summary>
    /// Deactivates the business unit (no DELETE in master data). Idempotent: deactivating an
    /// already inactive unit changes nothing and raises nothing. Returns whether anything changed.
    /// </summary>
    public bool Deactivate()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        RaiseEvent(new BusinessUnitDeactivated(Id, PartyId, LegalEntityId, Code, CaptionI18n, CostCenterCode, IsActive));
        return true;
    }

    private bool CaptionEquals(IReadOnlyDictionary<string, string> other) =>
        CaptionI18n.Count == other.Count
        && CaptionI18n.All(pair => other.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
}
