using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Npgsql;

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// The one transaction of a request (conventions-backend.md §4). Scoped, single-use and not
/// thread-safe. Lifecycle:
/// <list type="number">
///   <item><see cref="IRequestTransaction.BeginAsync"/>: opens a connection from <see cref="ServiceDataSource"/>
///   (role <c>svc_&lt;service&gt;</c>), <c>BEGIN</c>, then
///   <c>SELECT set_config('app.current_user_id', $1, true), set_config('app.correlation_id', $2, true)</c>
///   (transaction-local, parameterized; conventions-database.md §7).</item>
///   <item>The handler works on <see cref="Connection"/>/<see cref="Transaction"/>: Dapper
///   directly, EF Core through a <see cref="DbContextBase"/> (which enlists itself here).</item>
///   <item><see cref="IRequestTransaction.CommitAsync"/>: saves every enlisted DbContext, collects the domain events
///   of tracked aggregates plus those registered with <c>AddEvent*</c>, writes them to
///   <c>outbox.message</c> (validated against their <c>.avsc</c>, EV-R2) and only then
///   <c>COMMIT</c>s. Any failure rolls everything back and rethrows.</item>
/// </list>
/// Disposing an active unit of work that was not committed rolls it back.
/// <para>
/// <see cref="IRequestTransaction.IsActive"/>, <c>BeginAsync</c>, <c>CommitAsync</c> and <c>RollbackAsync</c> come
/// from <see cref="IRequestTransaction"/>, the Npgsql-free view of this same object that
/// application handlers depend on (step 0.4).
/// </para>
/// </summary>
public interface IUnitOfWork : IRequestTransaction, IAsyncDisposable
{
    /// <summary>The open connection of the active unit of work. Throws if not active.</summary>
    NpgsqlConnection Connection { get; }

    /// <summary>The transaction of the active unit of work. Throws if not active.</summary>
    NpgsqlTransaction Transaction { get; }

    /// <summary>
    /// Registers an event that is not raised by an aggregate tracked by an enlisted DbContext.
    /// <paramref name="aggregateVersion"/> becomes <c>outbox.message.aggregate_version</c> as is.
    /// <para>
    /// Intended for emitters that are not aggregates (consumers, jobs). If the event belongs to an
    /// aggregate whose table has <c>aggregate_version</c> (ADR-0019, conventions-database.md §4),
    /// the caller must persist and guard that version in this same transaction (e.g.
    /// <c>UPDATE … SET aggregate_version = @new WHERE id = @id AND aggregate_version = @loaded</c>
    /// and check the row count); otherwise versions repeat or race.
    /// </para>
    /// </summary>
    void AddEvent(IDomainEvent domainEvent, long aggregateVersion);

    /// <summary>
    /// Registers an aggregate so that its pending domain events are written on commit, with their
    /// versions, and cleared afterwards. Each aggregate instance is collected once, so registering
    /// one that an enlisted DbContext also tracks is harmless.
    /// <para>
    /// ADR-0019: the aggregate must be tracked (any state but Detached) by an enlisted
    /// <see cref="DbContextBase"/> at commit, because that is the only way its
    /// <c>aggregate_version</c> is persisted and guarded. <c>CommitAsync</c> throws
    /// <see cref="InvalidOperationException"/>, before any write and rolling back, if a
    /// registered aggregate with pending events is not tracked.
    /// </para>
    /// </summary>
    void AddEventsFrom<TId>(AggregateRoot<TId> aggregate)
        where TId : notnull;

    /// <summary>Called by <see cref="DbContextBase"/>'s constructor; not for handlers.</summary>
    void Enlist(DbContextBase dbContext);
}
