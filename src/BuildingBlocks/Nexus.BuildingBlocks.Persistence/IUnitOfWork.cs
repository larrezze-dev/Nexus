using Nexus.BuildingBlocks.Domain;
using Npgsql;

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// The one transaction of a request (conventions-backend.md §4). Scoped, single-use and not
/// thread-safe. Lifecycle:
/// <list type="number">
///   <item><see cref="BeginAsync"/>: opens a connection from <see cref="ServiceDataSource"/>
///   (role <c>svc_&lt;service&gt;</c>), <c>BEGIN</c>, then
///   <c>SELECT set_config('app.current_user_id', $1, true), set_config('app.correlation_id', $2, true)</c>
///   (transaction-local, parameterized; conventions-database.md §7).</item>
///   <item>The handler works on <see cref="Connection"/>/<see cref="Transaction"/>: Dapper
///   directly, EF Core through a <see cref="DbContextBase"/> (which enlists itself here).</item>
///   <item><see cref="CommitAsync"/>: saves every enlisted DbContext, collects the domain events
///   of tracked aggregates plus those registered with <c>AddEvent*</c>, writes them to
///   <c>outbox.message</c> (validated against their <c>.avsc</c>, EV-R2) and only then
///   <c>COMMIT</c>s. Any failure rolls everything back and rethrows.</item>
/// </list>
/// Disposing an active unit of work that was not committed rolls it back.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary><see langword="true"/> between a successful <see cref="BeginAsync"/> and commit/rollback.</summary>
    bool IsActive { get; }

    /// <summary>The open connection of the active unit of work. Throws if not active.</summary>
    NpgsqlConnection Connection { get; }

    /// <summary>The transaction of the active unit of work. Throws if not active.</summary>
    NpgsqlTransaction Transaction { get; }

    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an event that is not raised by an aggregate tracked by an enlisted DbContext
    /// (e.g. written with Dapper). <paramref name="aggregateVersion"/> becomes
    /// <c>outbox.message.aggregate_version</c>.
    /// </summary>
    void AddEvent(IDomainEvent domainEvent, long aggregateVersion);

    /// <summary>
    /// Registers an aggregate that is not tracked by an enlisted DbContext (e.g. persisted with
    /// Dapper) so that its pending domain events are written on commit, with their versions,
    /// and cleared afterwards. Registering an aggregate that is also tracked is harmless: each
    /// aggregate instance is collected once.
    /// </summary>
    void AddEventsFrom<TId>(AggregateRoot<TId> aggregate)
        where TId : notnull;

    /// <summary>Called by <see cref="DbContextBase"/>'s constructor; not for handlers.</summary>
    void Enlist(DbContextBase dbContext);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
