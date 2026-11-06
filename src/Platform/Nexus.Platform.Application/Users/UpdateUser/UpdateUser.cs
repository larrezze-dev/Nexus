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
/// Scope: the contract declares <c>x-scope: none</c>. Two independent checks, both before any
/// change:
/// <list type="number">
///   <item>Any edit of a user who holds role assignments requires the caller's
///   <c>USER:UPDATE</c> to cover every scope of them (<see cref="UserScopeGuard"/>): an admin
///   of one legal entity must not relabel (name, email, business unit) the admin of another.
///   A user with no assignment stays freely editable, as before.</item>
///   <item>Any actual change of <see cref="Oid"/> (null → value, value → other value,
///   value → null) additionally requires <c>USER:UPDATE</c> at <c>GLOBAL</c> scope: <c>oid</c>
///   is what maps an IdP login to this row (ADR-0006), so remapping it lets whoever owns the
///   new <c>oid</c> sign in <i>as</i> this user, with every role the user holds or will hold.
///   Check 1 does not subsume this: it covers <i>scopes</i>, not <i>roles</i> (an LE-A
///   ORG_ADMIN covers an LE-A user holding a role it does not have), and it has nothing to
///   check on a user with no assignment yet, who may be granted roles later.</item>
/// </list>
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
    private readonly IUserReadDb _readDb;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope": with scopeType GLOBAL it resolves to a target only a GLOBAL grant satisfies.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public UpdateUserHandler(
        IRequestTransaction transaction,
        IAppUserRepository users,
        IPartyRepository parties,
        IUserReadDb readDb,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _users = users;
        _parties = parties;
        _readDb = readDb;
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

        // Check 1: the target's own authority (every scope it holds an assignment in).
        if (!await UserScopeGuard.CoversEveryAssignmentScopeAsync(_readDb, _scopeChecker, UpdatePermission, user.Id, cancellationToken))
        {
            return Result.Failure<UserDetails>(UserErrors.ScopeNotCovered);
        }

        // Check 2: identity remapping safeguard. Same ordinal comparison the
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
