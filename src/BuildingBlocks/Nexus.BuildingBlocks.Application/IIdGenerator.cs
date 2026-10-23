namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Generates application-side identifiers. Application code must always obtain new ids
/// through this abstraction rather than calling <see cref="Guid"/> factory methods directly
/// (ADR-0009: UUIDv7, generated in the application, never in the database).
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}
