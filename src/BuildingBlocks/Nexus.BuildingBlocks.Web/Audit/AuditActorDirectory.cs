using Dapper;
using Nexus.BuildingBlocks.Persistence;

namespace Nexus.BuildingBlocks.Web.Audit;

/// <summary>
/// Resolves actor display names for one page of the audit trail: a single query on the request's
/// unit of work (<c>org</c> is a GLOBAL schema, readable by every <c>svc_*</c> role). The name is
/// <c>org.party.legal_name</c> of the user's party, the same value the users endpoint returns as
/// <c>fullName</c>. Users that no longer resolve (or <see cref="Guid.Empty"/>) are simply absent.
/// </summary>
internal static class AuditActorDirectory
{
    private const string Sql =
        """
        SELECT u.id AS Id, p.legal_name AS Name
        FROM org.app_user u
        JOIN org.party p ON p.id = u.party_id
        WHERE u.id = ANY(@ids)
        """;

    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        IUnitOfWork unitOfWork,
        IEnumerable<Guid> actorIds,
        CancellationToken cancellationToken)
    {
        var ids = actorIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await unitOfWork.Connection.QueryAsync<(Guid Id, string Name)>(
            new CommandDefinition(Sql, new { ids }, unitOfWork.Transaction, cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.Id, r => r.Name);
    }
}
