using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.Session.UpdateMe;

/// <summary>
/// <c>PATCH /v1/me</c> (<c>updateMe</c>): the caller changes its own
/// <see cref="PreferredLanguage"/>. No <c>If-Match</c> (conventions-api.md §5, the single
/// documented exception): it is the caller's own preference, idempotent, and the last write
/// wins. Returns the caller's profile as <c>GET /v1/me</c> reads it, with the new language.
/// <para>
/// Concurrency without a version from the client: the handler locks the caller's
/// <c>org.app_user</c> row before loading it (<see cref="IAppUserRepository.GetByIdForUpdateAsync"/>),
/// so concurrent PATCHes of the same user run one after the other, each on the row the previous
/// one committed: none of them can fail with a stale <c>xmin</c>/<c>aggregate_version</c> (409),
/// the final value is the one of the last to commit, and every change emits its own
/// <c>user.updated</c> with consecutive aggregate versions. An admin <c>PUT /v1/users/{id}</c>
/// whose <c>If-Match</c> was read before a PATCH committed still gets its 409 (the PATCH moved
/// <c>xmin</c>), as for any other concurrent change.
/// </para>
/// </summary>
public sealed record UpdateMeCommand(string? PreferredLanguage) : ICommand<CurrentUserProfile>;

internal sealed class UpdateMeValidator : AbstractValidator<UpdateMeCommand>
{
    public UpdateMeValidator()
    {
        RuleFor(c => c.PreferredLanguage).MustBeSupportedLanguage();
    }
}

internal sealed class UpdateMeHandler : ICommandHandler<UpdateMeCommand, CurrentUserProfile>
{
    private readonly IRequestTransaction _transaction;
    private readonly ICurrentUser _currentUser;
    private readonly IAppUserRepository _users;
    private readonly IUserReadDb _readDb;

    public UpdateMeHandler(
        IRequestTransaction transaction,
        ICurrentUser currentUser,
        IAppUserRepository users,
        IUserReadDb readDb)
    {
        _transaction = transaction;
        _currentUser = currentUser;
        _users = users;
        _readDb = readDb;
    }

    public async Task<Result<CurrentUserProfile>> Handle(UpdateMeCommand command, CancellationToken cancellationToken)
    {
        // Throws if UserId is not resolved: endpoints that reach here always have it (BE-R1).
        await _transaction.BeginAsync(cancellationToken);
        var userId = _currentUser.UserId!.Value;

        // Locked before it is read (see the class summary): no stale concurrency token possible.
        var user = await _users.GetByIdForUpdateAsync(userId, cancellationToken);

        // Deactivated (or gone) between the identity resolution and this lock: same answer as
        // GET /v1/me in that case (403 user_not_provisioned at the endpoint).
        if (user is null || !user.IsActive)
        {
            return Result.Failure<CurrentUserProfile>(Error.NotFound("No active app_user exists for this token."));
        }

        // Rule ORG-R6 is checked by the aggregate before anything changes. Raises user.updated
        // only if the value actually changes (same value: no write, no event).
        var changed = user.ChangePreferredLanguage(command.PreferredLanguage!);
        if (changed.IsFailure)
        {
            return Result.Failure<CurrentUserProfile>(changed.Error!);
        }

        // Read under the row lock, in this same transaction, before the commit saves the new
        // language: everything but the language is what will be committed; the language is the
        // aggregate's (it reached the database only at CommitAsync).
        var profile = await _readDb.GetActiveProfileAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{userId}' is locked and active but has no profile.");

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(profile with { PreferredLanguage = user.PreferredLanguage });
    }
}
