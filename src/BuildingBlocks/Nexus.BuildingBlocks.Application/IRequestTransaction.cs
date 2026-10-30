namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// The one transaction of the current scope, as command and query handlers see it
/// (conventions-backend.md §4). Implemented by <c>UnitOfWork</c>
/// (<c>Nexus.BuildingBlocks.Persistence</c>), whose <c>IUnitOfWork</c> extends this interface;
/// both resolve to the same scoped instance.
/// <para>
/// Why a separate port: handlers live in each service's <c>*.Application</c> project, which must
/// not depend on Npgsql/EF Core (conventions-backend.md §2). A handler only needs to open and
/// close the transaction; repositories and read databases (implemented in
/// <c>*.Infrastructure</c>) work on the unit of work's connection behind their own interfaces.
/// </para>
/// Usage in a handler:
/// <code>
/// await _transaction.BeginAsync(ct);   // RLS session: app.current_user_id, app.correlation_id
/// ... repositories / read db ...
/// await _transaction.CommitAsync(ct);  // saves, writes the outbox, COMMIT
/// return Result.Success(...);
/// </code>
/// Returning a failure without committing is enough to discard every change: the scoped unit
/// of work rolls back when the scope is disposed.
/// </summary>
public interface IRequestTransaction
{
    /// <summary><see langword="true"/> between a successful <see cref="BeginAsync"/> and commit/rollback.</summary>
    bool IsActive { get; }

    /// <summary>Opens the connection and transaction and stamps the RLS session (BE-R1).</summary>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves pending changes, writes their events to the outbox and commits, atomically.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Discards everything done in the transaction.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
