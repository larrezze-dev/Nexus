namespace Nexus.OutboxRelay;

/// <summary>
/// Aggregates held back because a topic they publish to is unreachable (missing or not
/// authorised), grouped by that topic. The batch query excludes them
/// (<see cref="IRelayLease.BeginBatchAsync(int, IReadOnlyCollection{Guid}, CancellationToken)"/>), so a
/// backlog of held head rows can neither fill every batch (starving the healthy topics) nor make
/// the loop spin.
///
/// <para>
/// <b>Holds do not expire while the topic is still unreachable.</b> Each topic has one probe time
/// (its negative-cache expiry, <see cref="PublishResult.HoldUntil"/>). When it passes, only the
/// topic's <i>probe aggregate</i> (the first one held, i.e. the oldest head row) is released from
/// the exclusion, so the next batch selects it and the publisher's preflight runs once for the
/// topic (one counted attempt on that row). Then:
/// <list type="bullet">
///   <item>still unreachable (<see cref="Hold"/> with a later time): every hold of the topic is
///   extended and the probe aggregate is held again — the other aggregates are never re-walked;</item>
///   <item>reachable (<see cref="ReleaseTopic"/>: a row of the topic was published or failed for a
///   reason that is not its topic): all of the topic's holds are released and their head rows are
///   selected again in <c>created_at, id</c> order (EV-R3).</item>
/// </list>
/// </para>
///
/// <para>
/// Bounded to <c>capacity</c> aggregates in total (<see cref="RelayOptions.MaxHeldAggregates"/>):
/// beyond it new holds are not recorded and those aggregates are selected and skipped
/// (<see cref="PublishResult.Deferred"/>) on every pass, so starvation of later rows can return —
/// visible through <c>nexus_outbox_held_aggregates</c> and <c>nexus_outbox_oldest_pending_age_seconds</c>.
/// In memory and per instance: a new leader rebuilds it within one walk of the backlog. Used by the
/// single relay loop only (not thread-safe).
/// </para>
/// </summary>
public sealed class HeldAggregates
{
    /// <summary>Default of <see cref="RelayOptions.MaxHeldAggregates"/>.</summary>
    public const int DefaultCapacity = 50_000;

    private readonly Dictionary<string, TopicHold> _topics = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private int _count;

    public HeldAggregates(TimeProvider timeProvider, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _timeProvider = timeProvider;
        _capacity = capacity;
    }

    /// <summary>Total holds (an aggregate held for two topics counts twice).</summary>
    public int Count => _count;

    /// <summary>Whether <paramref name="topic"/> currently has holds.</summary>
    public bool IsHeld(string topic) => _topics.ContainsKey(topic);

    /// <summary>
    /// Holds <paramref name="aggregateId"/> because <paramref name="topic"/> is unreachable until
    /// <paramref name="probeAt"/>; a later <paramref name="probeAt"/> extends every hold of the
    /// topic. Returns true when the aggregate was not held for this topic before (progress).
    /// </summary>
    public bool Hold(string topic, Guid aggregateId, DateTimeOffset probeAt)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (!_topics.TryGetValue(topic, out var hold))
        {
            if (_count >= _capacity)
            {
                return false;
            }

            hold = new TopicHold(aggregateId, probeAt);
            _topics[topic] = hold;
        }
        else if (probeAt > hold.ProbeAt)
        {
            hold.ProbeAt = probeAt;
        }

        if (hold.Aggregates.Contains(aggregateId) || _count >= _capacity)
        {
            return false;
        }

        hold.Aggregates.Add(aggregateId);
        _count++;
        return true;
    }

    /// <summary>The topic is reachable again: releases every hold of it.</summary>
    public void ReleaseTopic(string topic)
    {
        if (_topics.Remove(topic, out var hold))
        {
            _count -= hold.Aggregates.Count;
        }
    }

    /// <summary>
    /// The aggregates to exclude from the next batch: every held aggregate, except the probe
    /// aggregate of each topic whose probe time has passed.
    /// </summary>
    public IReadOnlyCollection<Guid> Current()
    {
        var now = _timeProvider.GetUtcNow();
        var result = new HashSet<Guid>();
        foreach (var hold in _topics.Values)
        {
            var probeDue = hold.ProbeAt <= now;
            foreach (var id in hold.Aggregates)
            {
                if (!(probeDue && id == hold.ProbeAggregate))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    private sealed class TopicHold(Guid probeAggregate, DateTimeOffset probeAt)
    {
        public Guid ProbeAggregate { get; } = probeAggregate;

        public DateTimeOffset ProbeAt { get; set; } = probeAt;

        public HashSet<Guid> Aggregates { get; } = [];
    }
}
