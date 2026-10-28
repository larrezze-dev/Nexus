using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.Session.GetCurrentUser;

/// <summary>
/// The NEXUS profile of the caller for <c>GET /v1/me</c>: reads its active
/// <c>org.app_user</c> by <see cref="ICurrentUser.UserId"/> (already resolved from the token's
/// <c>oid</c> before the endpoint runs) inside a regular unit of work. Fails with
/// <see cref="ErrorCodes.NotFound"/> if the user stopped being active in the meantime.
/// </summary>
public sealed record GetCurrentUserQuery : IQuery<CurrentUserProfile>;

internal sealed class GetCurrentUserHandler : IQueryHandler<GetCurrentUserQuery, CurrentUserProfile>
{
    private readonly IRequestTransaction _transaction;
    private readonly ICurrentUser _currentUser;
    private readonly IUserReadDb _readDb;

    public GetCurrentUserHandler(IRequestTransaction transaction, ICurrentUser currentUser, IUserReadDb readDb)
    {
        _transaction = transaction;
        _currentUser = currentUser;
        _readDb = readDb;
    }

    public async Task<Result<CurrentUserProfile>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        // Throws if UserId is not resolved: endpoints that reach here always have it (BE-R1).
        await _transaction.BeginAsync(cancellationToken);

        var profile = await _readDb.GetActiveProfileAsync(_currentUser.UserId!.Value, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return profile is null
            ? Result.Failure<CurrentUserProfile>(Error.NotFound("No active app_user exists for this token."))
            : Result.Success(profile);
    }
}
