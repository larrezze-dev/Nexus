namespace Nexus.BuildingBlocks.Messaging.Replica;

/// <summary>Outcome of <see cref="IVersionedReplica.ApplyAsync"/>.</summary>
public enum ReplicaApplyResult
{
    /// <summary>The row did not exist (inserted) or held a strictly lower <c>aggregate_version</c> (overwritten).</summary>
    Applied = 1,

    /// <summary>
    /// The row already holds the same or a higher <c>aggregate_version</c>: the event is older
    /// than (or a re-delivery of) what is applied, and nothing was written
    /// (conventions-events.md §7, "Orden").
    /// </summary>
    IgnoredStale = 2,
}
