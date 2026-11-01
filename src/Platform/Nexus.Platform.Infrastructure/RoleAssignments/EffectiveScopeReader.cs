using Dapper;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.RoleAssignments;
using Npgsql;

namespace Nexus.Platform.Infrastructure.RoleAssignments;

/// <summary>
/// <see cref="IEffectiveScopeReader"/>: the one query of this service that must see the world as
/// a <b>different</b> user than the caller would (domain/access.md §4, "¿qué ve este usuario?").
/// <para>
/// Why this bypasses <see cref="IUnitOfWork"/> entirely, instead of being another
/// <c>*ReadDb</c> on the request's own transaction: <see cref="UnitOfWork"/>
/// (<c>Nexus.BuildingBlocks.Persistence</c>) always stamps <c>app.current_user_id</c> to
/// <c>ICurrentUser.UserId</c> - the authenticated caller - because every other read/write in this
/// service must be evaluated under the caller's own RLS/scope session (BE-R1). This is the one
/// endpoint that must see what a DIFFERENT user would see, not what the caller holding
/// <c>ROLE_ASSIGNMENT:READ</c> would see, so it cannot reuse that machinery: it opens its own
/// connection directly off <see cref="ServiceDataSource"/> (the same data source
/// <see cref="UnitOfWork"/> itself uses), starts a dedicated read-only transaction
/// (<c>SET TRANSACTION READ ONLY</c>, the first statement after <c>BEGIN</c>), stamps
/// <c>app.current_user_id</c> to the <b>queried</b> user (the route's <c>{id}</c>, never
/// <c>ICurrentUser.UserId</c>), reads <c>access.effective_access_scope</c>, and always rolls
/// back (read-only; there is nothing to commit and no reason to risk leaving anything open).
/// </para>
/// Registered scoped, like everything else touching the database per request, but deliberately
/// holds no shared state with the request's own <see cref="IUnitOfWork"/>.
/// </summary>
internal sealed class EffectiveScopeReader : IEffectiveScopeReader
{
    private const string SetUserSql = "SELECT set_config('app.current_user_id', $1, true)";

    private const string EffectiveScopeSql =
        "SELECT dimension AS \"Dimension\", entity_id AS \"EntityId\" FROM access.effective_access_scope";

    private readonly ServiceDataSource _dataSource;

    public EffectiveScopeReader(ServiceDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<EffectiveScope> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
                {
                    await readOnly.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var setUser = new NpgsqlCommand(SetUserSql, connection, transaction))
                {
                    setUser.Parameters.Add(new NpgsqlParameter<string> { TypedValue = userId.ToString("D") });
                    await setUser.ExecuteNonQueryAsync(cancellationToken);
                }

                var rows = (await connection.QueryAsync<EffectiveScopeRow>(new CommandDefinition(
                    EffectiveScopeSql,
                    transaction: transaction,
                    cancellationToken: cancellationToken))).AsList();

                // Read-only: nothing was written, there is nothing to commit.
                await transaction.RollbackAsync(cancellationToken);

                return new EffectiveScope(
                    LegalEntityIds: Pick(rows, "LEGAL_ENTITY"),
                    BusinessUnitIds: Pick(rows, "BUSINESS_UNIT"),
                    WarehouseIds: Pick(rows, "WAREHOUSE"),
                    ProjectIds: Pick(rows, "PROJECT"));
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private static List<Guid> Pick(IReadOnlyList<EffectiveScopeRow> rows, string dimension) =>
        rows.Where(r => string.Equals(r.Dimension, dimension, StringComparison.Ordinal)).Select(r => r.EntityId).ToList();

    private sealed class EffectiveScopeRow
    {
        public string Dimension { get; set; } = string.Empty;

        public Guid EntityId { get; set; }
    }
}
