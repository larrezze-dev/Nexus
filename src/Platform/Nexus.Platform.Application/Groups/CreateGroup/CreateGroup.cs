using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Currencies;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Domain.Groups;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.Groups.CreateGroup;

/// <summary>
/// <c>POST /v1/groups</c> (<c>createGroup</c>): creates the group's internal
/// <c>ORGANIZATION</c> <see cref="Party"/> and the <see cref="Group"/> in one transaction
/// (ORG-R1, two aggregates in one command, the same documented exception as <c>CreateUser</c>).
/// <para>
/// Idempotent (conventions-api.md §5): with a client-supplied <see cref="Id"/> that already
/// exists, identical content → the existing group (<see cref="CreateGroupResult.Created"/> =
/// <see langword="false"/>, 200); different content → 409 <c>CONFLICT</c>.
/// </para>
/// </summary>
public sealed record CreateGroupCommand(
    Guid? Id,
    string LegalName,
    string? DisplayName,
    string Code,
    string ConsolidationMethod,
    string ReportingCurrencyCode) : ICommand<CreateGroupResult>;

/// <summary>The group and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateGroupResult(GroupDetails Group, bool Created);

internal sealed class CreateGroupValidator : AbstractValidator<CreateGroupCommand>
{
    private static readonly string[] AllowedConsolidationMethods = ["FULL", "PROPORTIONAL", "EQUITY"];

    public CreateGroupValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.LegalName).MustBeNonBlank();
        RuleFor(c => c.Code).MustBeNonBlank().Matches(@"^[A-Z0-9_\-\.]{1,40}$");
        RuleFor(c => c.ConsolidationMethod).Must(AllowedConsolidationMethods.Contains)
            .WithMessage("'consolidationMethod' must be one of: FULL, PROPORTIONAL, EQUITY.");
        RuleFor(c => c.ReportingCurrencyCode).Matches(@"^[A-Z]{3}$");
    }
}

internal sealed class CreateGroupHandler : ICommandHandler<CreateGroupCommand, CreateGroupResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly IGroupRepository _groups;
    private readonly IPartyRepository _parties;
    private readonly ICurrencyReadDb _currencies;
    private readonly IIdGenerator _ids;

    public CreateGroupHandler(
        IRequestTransaction transaction,
        IGroupRepository groups,
        IPartyRepository parties,
        ICurrencyReadDb currencies,
        IIdGenerator ids)
    {
        _transaction = transaction;
        _groups = groups;
        _parties = parties;
        _currencies = currencies;
        _ids = ids;
    }

    public async Task<Result<CreateGroupResult>> Handle(CreateGroupCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _groups.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        // Rule ORG-R7: the reporting currency must exist and be active.
        var currencyCheck = await CurrencyRules.EnsureActiveAsync(_currencies, command.ReportingCurrencyCode, cancellationToken);
        if (currencyCheck.IsFailure)
        {
            return Result.Failure<CreateGroupResult>(currencyCheck.Error!);
        }

        var partyResult = Party.Create(
            _ids.NewId(),
            PartyType.Organization,
            command.LegalName,
            command.DisplayName,
            taxId: null,
            erpCompanyCode: null,
            isInternal: true); // Rule ORG-R1: a Group's party is an internal ORGANIZATION.
        if (partyResult.IsFailure)
        {
            return Result.Failure<CreateGroupResult>(partyResult.Error!);
        }

        var party = partyResult.Value;
        var groupResult = Group.Create(
            command.Id ?? _ids.NewId(),
            party,
            command.Code,
            command.ConsolidationMethod,
            command.ReportingCurrencyCode);
        if (groupResult.IsFailure)
        {
            return Result.Failure<CreateGroupResult>(groupResult.Error!);
        }

        var group = groupResult.Value;
        _parties.Add(party);
        _groups.Add(group);

        // The code's uniqueness is enforced by the database at this point; its violation is
        // translated by the web layer (GroupErrors, AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateGroupResult(GroupDetails.From(group, _groups.GetMetadata(group)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateGroupResult>> ReplayAsync(CreateGroupCommand command, Group existing, CancellationToken cancellationToken)
    {
        var identical =
            string.Equals(existing.Code, command.Code, StringComparison.Ordinal)
            && string.Equals(existing.ConsolidationMethod, command.ConsolidationMethod, StringComparison.Ordinal)
            && string.Equals(existing.ReportingCurrencyCode, command.ReportingCurrencyCode, StringComparison.Ordinal);

        if (!identical)
        {
            return Result.Failure<CreateGroupResult>(GroupErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateGroupResult(GroupDetails.From(existing, _groups.GetMetadata(existing)), Created: false));
    }
}
