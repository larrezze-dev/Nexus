using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Currencies;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.Groups.UpdateGroup;

/// <summary>
/// <c>PUT /v1/groups/{id}</c> (<c>updateGroup</c>), full replacement guarded by
/// <c>If-Match</c> (<see cref="ExpectedVersion"/>). Never touches the group's party (the
/// contract's <c>UpdateGroupRequest</c> has no <c>legalName</c>/<c>displayName</c>; renaming it
/// goes through <c>PUT /v1/parties/{id}</c>).
/// </summary>
public sealed record UpdateGroupCommand(
    Guid Id,
    uint ExpectedVersion,
    string Code,
    string ConsolidationMethod,
    string ReportingCurrencyCode) : ICommand<GroupDetails>;

internal sealed class UpdateGroupValidator : AbstractValidator<UpdateGroupCommand>
{
    private static readonly string[] AllowedConsolidationMethods = ["FULL", "PROPORTIONAL", "EQUITY"];

    public UpdateGroupValidator()
    {
        RuleFor(c => c.Code).MustBeNonBlank().Matches(@"^[A-Z0-9_\-\.]{1,40}$");
        RuleFor(c => c.ConsolidationMethod).Must(AllowedConsolidationMethods.Contains)
            .WithMessage("'consolidationMethod' must be one of: FULL, PROPORTIONAL, EQUITY.");
        RuleFor(c => c.ReportingCurrencyCode).Matches(@"^[A-Z]{3}$");
    }
}

internal sealed class UpdateGroupHandler : ICommandHandler<UpdateGroupCommand, GroupDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IGroupRepository _groups;
    private readonly ICurrencyReadDb _currencies;

    public UpdateGroupHandler(IRequestTransaction transaction, IGroupRepository groups, ICurrencyReadDb currencies)
    {
        _transaction = transaction;
        _groups = groups;
        _currencies = currencies;
    }

    public async Task<Result<GroupDetails>> Handle(UpdateGroupCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var group = await _groups.GetByIdAsync(command.Id, cancellationToken);
        if (group is null)
        {
            return Result.Failure<GroupDetails>(GroupErrors.NotFound(command.Id));
        }

        var current = _groups.GetMetadata(group).Version;
        if (current != command.ExpectedVersion)
        {
            return Result.Failure<GroupDetails>(GroupErrors.StaleVersion);
        }

        // Rule ORG-R7: only re-checked if the currency actually changes.
        if (!string.Equals(group.ReportingCurrencyCode, command.ReportingCurrencyCode, StringComparison.Ordinal))
        {
            var currencyCheck = await CurrencyRules.EnsureActiveAsync(_currencies, command.ReportingCurrencyCode, cancellationToken);
            if (currencyCheck.IsFailure)
            {
                return Result.Failure<GroupDetails>(currencyCheck.Error!);
            }
        }

        var updated = group.Update(command.Code, command.ConsolidationMethod, command.ReportingCurrencyCode);
        if (updated.IsFailure)
        {
            return Result.Failure<GroupDetails>(updated.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(GroupDetails.From(group, _groups.GetMetadata(group)));
    }
}
