using Nexus.BuildingBlocks.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Nexus.BuildingBlocks.Web.Identity;

/// <summary>
/// Looks up the active <c>org.app_user</c> of an identity-provider subject (<c>oid</c>,
/// ADR-0006). Used by <see cref="IdentityResolutionMiddleware"/> only.
/// </summary>
public interface IAppUserDirectory
{
    /// <summary>
    /// The active user whose <c>external_subject</c> equals <paramref name="externalSubject"/>
    /// (exact, case-sensitive text comparison, the same way the column's UNIQUE constraint
    /// compares it), or <see langword="null"/> when there is none or it is deactivated.
    /// </summary>
    Task<AppUserIdentity?> FindActiveByExternalSubjectAsync(string externalSubject, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IAppUserDirectory"/> over the service's own <see cref="ServiceDataSource"/>.
/// <para>
/// This is the one documented exception to BE-R1 ("every query goes through the unit of
/// work"): the unit of work needs <c>app.current_user_id</c> before it can begin, and this
/// query is what produces that id, so it cannot run inside it. It is safe outside it because
/// <c>org</c> is a GLOBAL schema (no RLS, ORG-Q1), every <c>svc_*</c> role holds
/// <c>SELECT</c> on <c>org.app_user</c>, and it is a single read-only, parameterized statement
/// in autocommit mode — nothing to roll back, no session state left behind.
/// </para>
/// </summary>
internal sealed class NpgsqlAppUserDirectory : IAppUserDirectory
{
    private const string Sql =
        "SELECT id, party_id, business_unit_id FROM org.app_user WHERE external_subject = $1 AND is_active";

    private readonly ServiceDataSource _dataSource;

    public NpgsqlAppUserDirectory(ServiceDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<AppUserIdentity?> FindActiveByExternalSubjectAsync(string externalSubject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSubject);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(Sql, connection);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = externalSubject, NpgsqlDbType = NpgsqlDbType.Text });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new AppUserIdentity(
            reader.GetGuid(0),
            reader.GetGuid(1),
            await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetGuid(2));
    }
}
