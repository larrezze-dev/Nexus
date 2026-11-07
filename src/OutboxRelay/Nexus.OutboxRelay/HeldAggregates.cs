namespace Nexus.OutboxRelay;

/// <summary>
/// Aggregates held back because their topic is unreachable (missing or not authorised), each until
/// the topic's negative-cache entry expires (<see cref="PublishResult.HoldUntil"/>). The batch query
/// excludes them (<see cref="IRelayLease.BeginBatchAsync(int, IReadOnlyCollection{Guid}, CancellationToken)"/>),
/// so a set of held head rows as large as the batch can neither fill every batch (starving the
/// healthy topics) nor make the loop spin. When the hold expires the aggregate is selected again
/// and the preflight re-runs on it.
///
/// <para>
/// Bounded to <see cref="Capacity"/> aggregates: beyond it new holds are not recorded, and those
/// aggregates are simply selected and skipped (<see cref="PublishResult.Deferred"/>) as before,
/// which is still correct, only less efficient. In memory and per instance: a new leader rebuilds
/// it within one negative TTL. Used by the single relay loop only (not thread-safe).
/// </para>
/// </summary>
public sealed class HeldAggregates
{
    /// <summary>Maximum aggregates held at once.</summary>
    public const int Capacity = 10_000;

    private readonly Dictionary<Guid, DateTimeOffset> _until = [];
    private readonly TimeProvider _timeProvider;

    public HeldAggregates(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Holds <paramref name="aggregateId"/> until <paramref name="until"/> (keeps the later of two holds).</summary>
    public void Hold(Guid aggregateId, DateTimeOffset until)
    {
        if (_until.TryGetValue(aggregateId, out var current))
        {
            if (until > current)
            {
                _until[aggregateId] = until;
            }

            return;
        }

        if (_until.Count >= Capacity)
        {
            Prune(_timeProvider.GetUtcNow());
            if (_until.Count >= Capacity)
            {
                return;
            }
        }

        _until[aggregateId] = until;
    }

    /// <summary>The aggregates still held now (expired holds are dropped).</summary>
    public IReadOnlyCollection<Guid> Current()
    {
        Prune(_timeProvider.GetUtcNow());
        return [.. _until.Keys];
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (id, until) in _until.Where(e => e.Value <= now).ToList())
        {
            _until.Remove(id);
        }
    }
}
