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
/// time (its negative-cache expiry, <see cref="PublishResult.HoldUntil"/>). Once it has passed,
/// <see cref="Current"/> leaves the topic out of the exclusion list (the topic is <i>probing</i>), so
/// the next batch selects its oldest rows and the publisher's preflight runs once for the topic (one
/// counted attempt on the first row; the others come back <see cref="PublishResult.Deferred"/>).
/// The topic stays out of the list until a batch reaches one of its rows:
/// <list type="bullet">
///   <item>still unreachable (<see cref="Hold"/> with a later probe time): held again, excluded from
///   the next batches until the new probe time;</item>
///   <item>reachable (<see cref="ReleaseTopic"/>: a row of the topic was published, or failed for a
///   reason that is not its topic): released; its rows, and the later rows of their aggregates on
///   other topics, are selected again in <c>created_at, id</c> order (EV-R3).</item>
/// </list>
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
    private readonly Dictionary<string, DateTimeOffset> _probeAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _probing = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public HeldTopics(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>Number of held topics (probing ones included: they are still unreachable as far as the relay knows).</summary>
    public int Count => _probeAt.Count;

    /// <summary>Whether <paramref name="topic"/> is held.</summary>
    public bool IsHeld(string topic) => _probeAt.ContainsKey(topic);

    /// <summary>
    /// Holds <paramref name="topic"/> as unreachable until <paramref name="probeAt"/> (a later
    /// <paramref name="probeAt"/> moves the probe time forward, an earlier one is ignored).
    /// Returns true when the topic enters the exclusion list of the next batch because of this
    /// call — it was not held, or it was probing (left out of this batch by <see cref="Current"/>)
    /// and is still unreachable — i.e. the next batch advances past rows this one could not
    /// publish (the loop's drain rule, <see cref="BatchOutcome.NewHolds"/>). At most once per topic
    /// per batch.
    /// </summary>
    public bool Hold(string topic, DateTimeOffset probeAt)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (!_probeAt.TryGetValue(topic, out var current))
        {
            _probeAt.Add(topic, probeAt);
            return true;
        }

        if (probeAt > current)
        {
            _probeAt[topic] = probeAt;
        }

        // A probing topic that is still unreachable is excluded again from the next batch.
        return probeAt > _timeProvider.GetUtcNow() && _probing.Remove(topic);
    }

    /// <summary>The topic answered (a row was published or failed as Row): it is no longer held.</summary>
    public void ReleaseTopic(string topic)
    {
        _probeAt.Remove(topic);
        _probing.Remove(topic);
    }

    /// <summary>
    /// Drops every hold. Called when the instance (re)acquires leadership: another leader may have
    /// published the rows of a held topic meanwhile, so holds from a previous lease are not trusted.
    /// </summary>
    public void Clear()
    {
        _probeAt.Clear();
        _probing.Clear();
    }

    /// <summary>
    /// The topics to exclude from the next batch: the held topics whose probe time has not passed.
    /// The others are left out (probing) so the batch reaches their oldest rows and the publisher
    /// probes them. Never null and never contains null (the query needs a non-null <c>text[]</c>
    /// without NULL elements).
    /// </summary>
    public IReadOnlyCollection<string> Current()
    {
        var now = _timeProvider.GetUtcNow();
        var excluded = new List<string>(_probeAt.Count);
        _probing.Clear();
        foreach (var (topic, probeAt) in _probeAt)
        {
            if (probeAt > now)
            {
                excluded.Add(topic);
            }
            else
            {
                _probing.Add(topic);
            }
        }

        return excluded;
    }

    /// <summary>
    /// Defensive release, after a batch that came back with fewer than <c>BatchSize</c> rows (so
    /// every selectable row was in it): a topic left out by the last <see cref="Current"/> to be
    /// probed, with no row in <paramref name="batchRows"/>, has nothing selectable to probe (its rows
    /// are gone, or wait behind an earlier FAILED row or an earlier row on another held topic), so it
    /// is released instead of staying held with nothing to decide it. If rows of it appear later and
    /// it is still unreachable, the first batch that reaches them holds it again.
    /// </summary>
    public void ReleaseUnprobeableTopics(IEnumerable<OutboxMessage> batchRows)
    {
        ArgumentNullException.ThrowIfNull(batchRows);
        if (_probing.Count == 0)
        {
            return;
        }

        var present = batchRows.Select(m => m.Topic).ToHashSet(StringComparer.Ordinal);
        foreach (var topic in _probing.ToList())
        {
            if (!present.Contains(topic))
            {
                ReleaseTopic(topic);
            }
        }

        _probing.Clear();
    }
}
