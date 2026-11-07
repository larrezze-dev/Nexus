using Npgsql;

namespace Nexus.BuildingBlocks.Messaging.Replica;

/// <summary>
/// Ordering helper for consumers that maintain a local <c>*_ref</c> replica of another
/// service's master data (conventions-events.md §7, "Orden"; ficha 0.7 track S).
///
/// <para>Applies the full state carried by a master-data event (EV-R4) only if its
/// <c>aggregate_version</c> is strictly greater than the one already stored; an older or equal
/// version is discarded (<see cref="ReplicaApplyResult.IgnoredStale"/>). The comparison and the
/// write are a single <c>INSERT ... ON CONFLICT ... DO UPDATE ... WHERE</c> statement, so the
/// check is atomic and race-safe across partitions and consumer instances: PostgreSQL locks the
/// conflicting row and, under READ COMMITTED (the <c>UnitOfWork</c>'s level), re-evaluates the
/// <c>WHERE</c> against its latest committed version. Two concurrent first inserts of the same
/// key serialise on the unique index and the loser takes the update path.</para>
///
/// <para>It runs inside the caller's transaction (the <c>UnitOfWork</c>'s), so the replica write,
/// the <c>inbox.processed_message</c> row and any other effect commit or roll back together. A
/// stale event is still a successfully processed event: the consumer records it in the inbox
/// and commits as usual.</para>
///
/// <para>Row-level security: if the replica table has RLS, the existing row must be visible to
/// the caller's UPDATE policy, otherwise PostgreSQL raises an error on the conflict path (it
/// never silently skips). Consumers that write <c>*_ref</c> tables run with their own service
/// role and the session context the consumer base sets.</para>
/// </summary>
public interface IVersionedReplica
{
    /// <summary>The replica table this instance writes.</summary>
    ReplicaTable Table { get; }

    /// <summary>
    /// Inserts or overwrites the row keyed by <paramref name="id"/> with
    /// <paramref name="values"/> if <paramref name="aggregateVersion"/> is greater than the
    /// stored one.
    /// </summary>
    /// <param name="transaction">An active transaction (normally <c>IUnitOfWork.Transaction</c>).</param>
    /// <param name="id">The event's <c>aggregate_id</c>.</param>
    /// <param name="aggregateVersion">The event's <c>nexus-aggregate-version</c> (non-negative).</param>
    /// <param name="values">One entry per <see cref="ReplicaTable.Columns"/>, no more and no less
    /// (full state, EV-R4). <c>null</c> writes SQL <c>NULL</c>.</param>
    Task<ReplicaApplyResult> ApplyAsync(
        NpgsqlTransaction transaction,
        Guid id,
        long aggregateVersion,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);
}
