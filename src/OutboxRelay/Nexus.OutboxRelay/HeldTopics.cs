namespace Nexus.OutboxRelay;

/// <summary>
/// Topics held back because they are unreachable (missing or not authorised). The batch query
/// excludes them (<see cref="IRelayLease.BeginBatchAsync(int, IReadOnlyCollection{string}, CancellationToken)"/>):
/// every PENDING row on a held topic, and every later PENDING row of an aggregate that has an
/// earlier PENDING row on a held topic (EV-R3). So a backlog of rows on an unreachable topic can
/// neither fill every batch (starving the healthy topics) nor make the loop spin, however many
/// aggregates it spans: the state is one entry per topic, with no per-aggregate list and no cap.
///
/// <para>
/// <b>Holds do not expire while the topic is still unreachable.</b> Each held topic has one probe
/// time (its negative-cache expiry, <see cref="PublishResult.HoldUntil"/>). Once it has passed the
/// topic is <i>due</i>, and a later <see cref="Current"/> leaves it out of the exclusion list (the
/// topic is <i>probing</i> for that batch), so the batch selects its oldest rows and the publisher's
/// preflight runs once for the topic (one counted attempt on the first row; the others come back
/// <see cref="PublishResult.Deferred"/>). Then:
/// <list type="bullet">
///   <item>still unreachable (<see cref="Hold"/> with a later probe time): held again, excluded from
///   the next batches until the new probe time;</item>
///   <item>reachable (<see cref="ReleaseTopic"/>: a row of the topic was published, or failed for a
///   reason that is not its topic): released; its rows, and the later rows of their aggregates on
///   other topics, are selected again in <c>created_at, id</c> order (EV-R3);</item>
///   <item>not reached (the batch was full of older rows, stopped on an InfraGlobal failure, or
///   skipped the topic's rows by EV-R3): still due, probed again later (see the order below).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>One topic per probe batch, least recently probed first.</b> A probing topic makes all its rows
/// selectable and the batch is ordered by <c>created_at, id</c>: with several topics left out at
/// once, the oldest backlog would fill the batch and the same oldest topics would take every probe
/// while the others were never probed again. So <see cref="Current"/> leaves out only one due topic,
/// by a single LRU key: the time of its last try in this due period (left out without an answer:
/// its row was not reached) or, if not tried yet, its probe time; then topic name (ordinal). A
/// re-held topic gets a new probe time (now + negative TTL) and goes to the back; a topic tried
/// without an answer goes behind every topic that became due before that try.
/// </para>
///
/// <para>
/// <b>Fairness between probes and the rest of the backlog.</b> A probe that finds the topic still
/// unreachable is progress (the loop drains on at once). Probe batches never follow each other:
/// after a batch for which <see cref="Current"/> left a topic out, the next <see cref="Current"/>
/// excludes every held topic, due or not, so at least every other batch is free of probes and the
/// healthy rows behind many unreachable topics always get batches. The loop drains on at once while
/// a due topic has not been tried yet (<see cref="HasUntriedDueTopic"/>): every other batch is a
/// probe batch, and each probe moves the chosen topic behind every topic due at that moment, so all
/// of them are tried within N probe batches; these drains are bounded by about 2N batches per
/// negative TTL, and never spin on a topic whose rows cannot be reached (once every due topic has
/// been tried, the loop waits for the poll like the rest). Every topic ahead of a due topic in the LRU
/// order has an older key and every topic re-held or tried after it goes behind it, so every due
/// topic is probed within N probe/fairness cycles (N = held topics), and still at most one counted
/// attempt per topic per negative TTL. With <i>N × 2 × batch duration</i> ≥ the negative TTL the loop
/// chains batches without pausing for the poll (half probes, half healthy rows), bounded to about
/// 2N batches per TTL. The trade-off: with many unreachable topics, a topic that becomes reachable
/// is noticed up to N cycles after its probe time instead of at once.
/// </para>
///
/// <para>
/// In memory and per relay instance, cleared (<see cref="Clear"/>) whenever the instance acquires
/// leadership: another leader may have published the topic's rows meanwhile, and a new leader
/// rebuilds the holds within one batch per unreachable topic. Used by the single relay loop only
/// (not thread-safe).
/// </para>
/// </summary>
public sealed class HeldTopics
{
    private readonly Dictionary<string, TopicHold> _topics = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private string? _probing;
    private bool _lastWasProbeBatch;

    public HeldTopics(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>Number of held topics (probing ones included: they are still unreachable as far as the relay knows).</summary>
    public int Count => _topics.Count;

    /// <summary>Whether <paramref name="topic"/> is held.</summary>
    public bool IsHeld(string topic) => _topics.ContainsKey(topic);

    /// <summary>
    /// Holds <paramref name="topic"/> as unreachable until <paramref name="probeAt"/> (a later
    /// <paramref name="probeAt"/> moves the probe time forward and starts a new due period; an
    /// earlier one is ignored). Returns true when the topic enters the exclusion list of the next
    /// batch because of this call — it was not held, or it was the probing topic of this batch and is
    /// still unreachable — i.e. the next batch advances past rows this one could not publish (the
    /// loop's drain rule, <see cref="BatchOutcome.NewHolds"/>). At most once per topic per batch.
    /// </summary>
    public bool Hold(string topic, DateTimeOffset probeAt)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (!_topics.TryGetValue(topic, out var hold))
        {
            _topics.Add(topic, new TopicHold(probeAt));
            return true;
        }

        var now = _timeProvider.GetUtcNow();
        if (probeAt > hold.ProbeAt)
        {
            hold.ProbeAt = probeAt;
            if (probeAt > now)
            {
                // A new due period: the topic goes to the back of the probe order.
                hold.TriedAt = null;
            }
        }

        // The probing topic, still unreachable, is excluded again from the next batch.
        if (probeAt > now && string.Equals(_probing, topic, StringComparison.Ordinal))
        {
            _probing = null;
            return true;
        }

        return false;
    }

    /// <summary>The topic answered (a row was published or failed as Row): it is no longer held.</summary>
    public void ReleaseTopic(string topic)
    {
        _topics.Remove(topic);
        if (string.Equals(_probing, topic, StringComparison.Ordinal))
        {
            _probing = null;
        }
    }

    /// <summary>
    /// Drops every hold. Called when the instance (re)acquires leadership: another leader may have
    /// published the rows of a held topic meanwhile, so holds from a previous lease are not trusted.
    /// </summary>
    public void Clear()
    {
        _topics.Clear();
        _probing = null;
        _lastWasProbeBatch = false;
    }

    /// <summary>
    /// The topics to exclude from the next batch (call once per batch): every held topic but, when
    /// probes are allowed (the previous batch was not a probe batch), the one due topic that is next
    /// in probe order (see the class remarks), which is left out so the batch reaches its oldest rows.
    /// Never null and never contains null (the query needs a non-null <c>text[]</c> without NULL
    /// elements).
    /// </summary>
    public IReadOnlyCollection<string> Current()
    {
        var now = _timeProvider.GetUtcNow();
        _probing = null;
        if (!_lastWasProbeBatch)
        {
            string? next = null;
            TopicHold? nextHold = null;
            foreach (var (topic, hold) in _topics)
            {
                if (hold.ProbeAt <= now && (nextHold is null || ProbesBefore(topic, hold, next!, nextHold)))
                {
                    (next, nextHold) = (topic, hold);
                }
            }

            if (next is not null)
            {
                nextHold!.TriedAt = now;
                _probing = next;
            }
        }

        _lastWasProbeBatch = _probing is not null;
        var excluded = new List<string>(_topics.Count);
        foreach (var topic in _topics.Keys)
        {
            if (!string.Equals(topic, _probing, StringComparison.Ordinal))
            {
                excluded.Add(topic);
            }
        }

        return excluded;
    }

    /// <summary>
    /// Whether a due topic has not been tried since it became due: the loop then drains on at once
    /// instead of waiting for the poll. Every other batch is a probe batch and each probe moves its
    /// topic behind every topic due at that moment, so all such topics are tried within N probe
    /// batches (N = held topics): these drains are bounded by about 2N batches per negative TTL.
    /// </summary>
    public bool HasUntriedDueTopic()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var hold in _topics.Values)
        {
            if (hold.ProbeAt <= now && hold.TriedAt is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Defensive release, after a batch that came back with fewer than <c>BatchSize</c> rows (so
    /// every selectable row was in it): the topic that the last <see cref="Current"/> left out to be
    /// probed, if it has no row in <paramref name="batchRows"/>, has nothing selectable to probe (its
    /// rows are gone, or wait behind an earlier FAILED row or an earlier row on another held topic),
    /// so it is released instead of staying held with nothing to decide it. If rows of it appear later
    /// and it is still unreachable, the first batch that reaches them holds it again. A batch with no
    /// probing topic releases nothing.
    /// </summary>
    public void ReleaseUnprobeableTopics(IEnumerable<OutboxMessage> batchRows)
    {
        ArgumentNullException.ThrowIfNull(batchRows);
        if (_probing is not { } probing)
        {
            return;
        }

        if (!batchRows.Any(m => string.Equals(m.Topic, probing, StringComparison.Ordinal)))
        {
            ReleaseTopic(probing);
        }

        _probing = null;
    }

    /// <summary>
    /// Probe order, a single least-recently-probed key for every due topic: the time of its last
    /// try in this due period or, if not tried yet, its probe time; then topic name (ordinal). No
    /// priority for untried topics: under saturation some topic is always untried, and a topic tried
    /// without an answer would never be chosen again.
    /// </summary>
    private static bool ProbesBefore(string topic, TopicHold hold, string other, TopicHold otherHold)
    {
        var key = hold.TriedAt ?? hold.ProbeAt;
        var otherKey = otherHold.TriedAt ?? otherHold.ProbeAt;
        return key != otherKey ? key < otherKey : string.CompareOrdinal(topic, other) < 0;
    }

    private sealed class TopicHold(DateTimeOffset probeAt)
    {
        public DateTimeOffset ProbeAt { get; set; } = probeAt;

        /// <summary>When it was last left out to be probed in its current due period (null: not yet).</summary>
        public DateTimeOffset? TriedAt { get; set; }
    }
}
