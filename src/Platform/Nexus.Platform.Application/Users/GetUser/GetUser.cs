using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Users.GetUser;

/// <summary><c>GET /v1/users/{id}</c> (<c>getUser</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetUserQuery(Guid Id) : IQuery<UserDetails>;

internal sealed class GetUserHandler : IQueryHandler<GetUserQuery, UserDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IUserReadDb _readDb;

    public GetUserHandler(IRequestTransaction transaction, IUserReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<UserDetails>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        // BE-R1: reads go through the unit of work too (RLS session), even on a GLOBAL schema.
        await _transaction.BeginAsync(cancellationToken);

        var user = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return user is null
            ? Result.Failure<UserDetails>(UserErrors.NotFound(query.Id))
            : Result.Success(user);
    }
}
