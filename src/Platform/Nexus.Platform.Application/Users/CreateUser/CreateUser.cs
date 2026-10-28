using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Application.Users.CreateUser;

/// <summary>
/// <c>POST /v1/users</c> (<c>createUser</c>): creates the user's internal <c>PERSON</c>
/// <see cref="Party"/> and the <see cref="AppUser"/> in one transaction.
/// <para>
/// Two aggregates in one command, a documented exception to "one aggregate per command"
/// (conventions-backend.md §6; ficha 0.4 track B): the person party of a user has no life of
/// its own outside it — same pattern as the internal party of Group/LegalEntity/BusinessUnit.
/// </para>
/// <para>
/// Idempotent (conventions-api.md §5): with a client-supplied <see cref="Id"/> that already
/// exists, identical content → the existing user (<see cref="CreateUserResult.Created"/> =
/// <see langword="false"/>, 200); different content → 409 <c>CONFLICT</c>.
/// </para>
/// </summary>
public sealed record CreateUserCommand(
    Guid? Id,
    string Oid,
    string FullName,
    string? Email,
    string PreferredLanguage,
    Guid? BusinessUnitId) : ICommand<CreateUserResult>;

/// <summary>The user and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateUserResult(UserDetails User, bool Created);

internal sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.Oid).MustBeNonBlank();
        RuleFor(c => c.FullName).MustBeNonBlank();
        RuleFor(c => c.PreferredLanguage).MustBeSupportedLanguage();
        When(c => c.Email is not null, () => RuleFor(c => c.Email).MustBeNonBlank().EmailAddress());
        RuleFor(c => c.BusinessUnitId).Must(id => id != Guid.Empty).WithMessage("'businessUnitId' must not be an empty UUID.");
    }
}

internal sealed class CreateUserHandler : ICommandHandler<CreateUserCommand, CreateUserResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly IAppUserRepository _users;
    private readonly IPartyRepository _parties;
    private readonly IIdGenerator _ids;

    public CreateUserHandler(
        IRequestTransaction transaction,
        IAppUserRepository users,
        IPartyRepository parties,
        IIdGenerator ids)
    {
        _transaction = transaction;
        _users = users;
        _parties = parties;
        _ids = ids;
    }

    public async Task<Result<CreateUserResult>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        // Rule ORG-R6 first: reusing SYSTEM's id must read as ORG-R6, not as an idempotency conflict.
        if (command.Id is { } id && AppUser.EnsureNotSystem(id) is { IsFailure: true } system)
        {
            return Result.Failure<CreateUserResult>(system.Error!);
        }

        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _users.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        var partyResult = Party.Create(
            _ids.NewId(),
            PartyType.Person,
            command.FullName,
            displayName: null,
            taxId: null,
            erpCompanyCode: null,
            isInternal: true); // a user is a person of the group (same as every seeded user)
        if (partyResult.IsFailure)
        {
            return Result.Failure<CreateUserResult>(partyResult.Error!);
        }

        var party = partyResult.Value;
        var userResult = AppUser.Create(
            command.Id ?? _ids.NewId(),
            party,
            command.Oid,
            command.Email,
            command.PreferredLanguage,
            command.BusinessUnitId);
        if (userResult.IsFailure)
        {
            return Result.Failure<CreateUserResult>(userResult.Error!);
        }

        var user = userResult.Value;
        _parties.Add(party);
        _users.Add(user);

        // ORG-R9 (email unique among active users), the oid's uniqueness and the business
        // unit's existence are enforced by the database at this point; their violations are
        // translated by the web layer (UserErrors, AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateUserResult(
            UserDetails.From(user, _users.GetMetadata(user), party, _parties.GetMetadata(party)),
            Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateUserResult>> ReplayAsync(CreateUserCommand command, AppUser existing, CancellationToken cancellationToken)
    {
        var party = await _parties.GetByIdAsync(existing.PartyId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{existing.Id}' references a missing party '{existing.PartyId}'.");

        var identical =
            string.Equals(existing.ExternalSubject, command.Oid, StringComparison.Ordinal)
            && string.Equals(party.LegalName, command.FullName, StringComparison.Ordinal)
            && string.Equals(existing.Email, command.Email, StringComparison.Ordinal)
            && string.Equals(existing.PreferredLanguage, command.PreferredLanguage, StringComparison.Ordinal)
            && existing.BusinessUnitId == command.BusinessUnitId;

        if (!identical)
        {
            return Result.Failure<CreateUserResult>(UserErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateUserResult(
            UserDetails.From(existing, _users.GetMetadata(existing), party, _parties.GetMetadata(party)),
            Created: false));
    }
}
