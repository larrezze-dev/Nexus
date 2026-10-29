using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Domain.Groups;

/// <summary>
/// <c>org.&quot;group&quot;</c> (domain/org.md §3): the top of the group's organisational structure.
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

    /// <summary>
    /// Creates a group for <paramref name="party"/> and raises <see cref="GroupCreated"/>.
    /// The currency's existence/activity (ORG-R7) is checked by the calling command before this
    /// is called (no database access from the aggregate); here only ORG-R1 is enforced.
    /// </summary>
    public static Result<Group> Create(
        Guid id,
        Party party,
        string code,
        string consolidationMethod,
        string reportingCurrencyCode)
    {
        Guard.Against.Null(party, nameof(party));
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.NullOrWhiteSpace(consolidationMethod, nameof(consolidationMethod));
        Guard.Against.NullOrWhiteSpace(reportingCurrencyCode, nameof(reportingCurrencyCode));

        var org1 = EnsureInternalOrganizationParty(party);
        if (org1.IsFailure)
        {
            return Result.Failure<Group>(org1.Error!);
        }

        var group = new Group(id)
        {
            PartyId = party.Id,
            Code = code,
            ConsolidationMethod = consolidationMethod,
            ReportingCurrencyCode = reportingCurrencyCode,
        };

        group.RaiseEvent(new GroupCreated(
            group.Id, group.PartyId, group.Code, group.ConsolidationMethod, group.ReportingCurrencyCode));

        return Result.Success(group);
    }

    /// <summary>
    /// Replaces <see cref="Code"/>, <see cref="ConsolidationMethod"/> and
    /// <see cref="ReportingCurrencyCode"/> (PUT semantics). The new currency's activity
    /// (ORG-R7) is checked by the calling command before this is called. Returns whether
    /// anything changed; raises <see cref="GroupUpdated"/> only in that case.
    /// </summary>
    public Result<bool> Update(string code, string consolidationMethod, string reportingCurrencyCode)
    {
        Guard.Against.NullOrWhiteSpace(code, nameof(code));
        Guard.Against.NullOrWhiteSpace(consolidationMethod, nameof(consolidationMethod));
        Guard.Against.NullOrWhiteSpace(reportingCurrencyCode, nameof(reportingCurrencyCode));

        var changed =
            !string.Equals(Code, code, StringComparison.Ordinal)
            || !string.Equals(ConsolidationMethod, consolidationMethod, StringComparison.Ordinal)
            || !string.Equals(ReportingCurrencyCode, reportingCurrencyCode, StringComparison.Ordinal);

        if (!changed)
        {
            return Result.Success(false);
        }

        Code = code;
        ConsolidationMethod = consolidationMethod;
        ReportingCurrencyCode = reportingCurrencyCode;

        RaiseEvent(new GroupUpdated(Id, PartyId, Code, ConsolidationMethod, ReportingCurrencyCode));
        return Result.Success(true);
    }

    /// <summary>Rule ORG-R1: <paramref name="party"/> must be an internal <c>ORGANIZATION</c>.</summary>
    internal static Result EnsureInternalOrganizationParty(Party party) =>
        party.PartyType != PartyType.Organization || !party.IsInternal
            ? Result.Failure(new Error("ORG-R1", "A Group's party must be an internal ORGANIZATION."))
            : Result.Success();
}
