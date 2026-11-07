using Nexus.BuildingBlocks.Messaging.Inbox;

namespace Nexus.BuildingBlocks.Messaging;

/// <summary>Consumer settings. Group id is <c>&lt;Service&gt;.&lt;UseCase&gt;</c>.</summary>
public sealed class ConsumerOptions
{
    public required string Service { get; init; }
    public required string UseCase { get; init; }
    public required string Topic { get; init; }
    public required string BootstrapServers { get; init; }
    public required string SchemaRegistryUrl { get; init; }

    /// <summary>
    /// Identity the consumer's transaction runs under (conventions-events.md §7: the step's ficha
    /// says which). Defaults to <see cref="ActingIdentity.System"/>: acting on behalf of the
    /// event's actor must be an explicit choice.
    /// </summary>
    public ActingIdentity Identity { get; init; } = ActingIdentity.System;

    /// <summary><c>max.poll.interval.ms</c> (see the time budget in <see cref="NexusKafkaOptions"/>). Default 5 min.</summary>
    public TimeSpan MaxPollInterval { get; init; } = TimeSpan.FromMinutes(5);

    public string GroupId => $"{Service}.{UseCase}";
}
