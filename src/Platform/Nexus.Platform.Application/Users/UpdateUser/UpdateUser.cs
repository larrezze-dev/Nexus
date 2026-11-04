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
/// <para>
/// Scope: the contract declares <c>x-scope: none</c> and an ordinary edit stays unscoped (a
/// user has no single owning legal entity: <c>businessUnitId</c> is optional). Changing
/// <see cref="Oid"/> is different: <c>oid</c> is what maps an IdP login to this row (ADR-0006),
/// so remapping it lets the caller (or whoever owns the new <c>oid</c>) sign in <i>as</i> this
/// user, with every role the user holds -- including a GLOBAL <c>SYSTEM_ADMIN</c>. Any actual
/// change of <c>oid</c> (null → value, value → other value, value → null) therefore requires the
/// caller's <c>USER:UPDATE</c> to be held at <c>GLOBAL</c> scope, and answers 403 otherwise.
/// An unchanged <c>oid</c> skips the check.
/// </para>
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
    private const string UpdatePermission = "USER:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IAppUserRepository _users;
    private readonly IPartyRepository _parties;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope": with scopeType GLOBAL it resolves to a target only a GLOBAL grant satisfies.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public UpdateUserHandler(
        IRequestTransaction transaction,
        IAppUserRepository users,
        IPartyRepository parties,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _users = users;
        _parties = parties;
        _scopeChecker = scopeChecker;
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

        // Identity remapping safeguard, before anything changes. Same ordinal comparison the
        // aggregate uses to detect a change; both null is "unchanged".
        if (!string.Equals(user.ExternalSubject, command.Oid, StringComparison.Ordinal)
            && !await _scopeChecker.HasPermissionCoveringAsync(UpdatePermission, "GLOBAL", null, cancellationToken))
        {
            return Result.Failure<UserDetails>(UserErrors.OidChangeRequiresGlobal);
        }

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
