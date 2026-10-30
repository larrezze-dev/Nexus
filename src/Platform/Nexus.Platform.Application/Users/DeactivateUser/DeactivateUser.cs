using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Parties;

namespace Nexus.Platform.Application.Users.DeactivateUser;

/// <summary>
/// <c>POST /v1/users/{id}/deactivate</c> (<c>deactivateUser</c>), guarded by <c>If-Match</c>.
/// Never for SYSTEM (ORG-R6). Idempotent: an already inactive user is returned unchanged,
/// without a new event. The person party stays active (it remains in the directory).
/// </summary>
public sealed record DeactivateUserCommand(Guid Id, UserVersion ExpectedVersion) : ICommand<UserDetails>;

internal sealed class DeactivateUserHandler : ICommandHandler<DeactivateUserCommand, UserDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IAppUserRepository _users;
    private readonly IPartyRepository _parties;

    public DeactivateUserHandler(IRequestTransaction transaction, IAppUserRepository users, IPartyRepository parties)
    {
        _transaction = transaction;
        _users = users;
        _parties = parties;
    }

    public async Task<Result<UserDetails>> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await UserLoader.LoadAsync(_users, _parties, command.Id, command.ExpectedVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<UserDetails>(loaded.Error!);
        }

        var (user, party) = loaded.Value;

        var deactivated = user.Deactivate();
        if (deactivated.IsFailure)
        {
            return Result.Failure<UserDetails>(deactivated.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(UserDetails.From(user, _users.GetMetadata(user), party, _parties.GetMetadata(party)));
    }
}
