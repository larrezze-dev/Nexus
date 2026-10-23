namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Default <see cref="IIdGenerator"/>: UUIDv7 derived from an injected <see cref="TimeProvider"/>
/// (ADR-0009) so that id generation is deterministic and testable, same as every other
/// time-dependent concern in the codebase (CLAUDE.md §3 "Tiempo e ids").
/// </summary>
internal sealed class UuidV7IdGenerator : IIdGenerator
{
    private readonly TimeProvider _timeProvider;

    public UuidV7IdGenerator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Guid NewId() => Guid.CreateVersion7(_timeProvider.GetUtcNow());
}
