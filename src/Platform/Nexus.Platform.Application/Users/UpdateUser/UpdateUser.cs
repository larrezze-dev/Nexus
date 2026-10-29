using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Parties;

namespace Nexus.Platform.Application.Users.UpdateUser;

/// <summary>
/// <c>PUT /v1/users/{id}</c> (<c>updateUser</c>), full replacement guarded by
/// <c>If-Match</c> (<see cref="ExpectedVersion"/>). <see cref="FullName"/> renames the user's
/// person party in the same transaction (the same two-aggregate exception as the create).
/// A <see langword="null"/> <see cref="Oid"/> means "cannot sign in" (contract
/// <c>UpdateUserRequest.oid</c>), not "unchanged".
/// </summary>
public sealed record UpdateUserCommand(
    Guid Id,
    UserVersion ExpectedVersion,
    string? Oid,
    string FullName,
    string? Email,
    string PreferredLanguage,
    Guid? BusinessUnitId) : ICommand<UserDetails>;

internal sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        When(c => c.Oid is not null, () => RuleFor(c => c.Oid).MustBeNonBlank());
        RuleFor(c => c.FullName).MustBeNonBlank();
        RuleFor(c => c.PreferredLanguage).MustBeSupportedLanguage();
        When(c => c.Email is not null, () => RuleFor(c => c.Email).MustBeNonBlank().EmailAddress());
        RuleFor(c => c.BusinessUnitId).Must(id => id != Guid.Empty).WithMessage("'businessUnitId' must not be an empty UUID.");
    }
}

internal sealed class UpdateUserHandler : ICommandHandler<UpdateUserCommand, UserDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IAppUserRepository _users;
    private readonly IPartyRepository _parties;

    public UpdateUserHandler(IRequestTransaction transaction, IAppUserRepository users, IPartyRepository parties)
    {
        _transaction = transaction;
        _users = users;
        _parties = parties;
    }

    public async Task<Result<UserDetails>> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await UserLoader.LoadAsync(_users, _parties, command.Id, command.ExpectedVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<UserDetails>(loaded.Error!);
        }

        var (user, party) = loaded.Value;

        // Rule ORG-R6 is checked by the aggregate before anything changes, so the SYSTEM
        // user's party is never renamed either.
        var updated = user.Update(command.Oid, command.Email, command.PreferredLanguage, command.BusinessUnitId);
        if (updated.IsFailure)
        {
            return Result.Failure<UserDetails>(updated.Error!);
        }

        party.Rename(command.FullName);

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(UserDetails.From(user, _users.GetMetadata(user), party, _parties.GetMetadata(party)));
    }
}
