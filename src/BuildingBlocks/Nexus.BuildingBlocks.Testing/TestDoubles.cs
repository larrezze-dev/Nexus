using Nexus.BuildingBlocks.Application;

namespace Nexus.BuildingBlocks.Testing;

/// <summary>Minimal <see cref="ICurrentUser"/> for tests that drive a real <c>UnitOfWork</c>.</summary>
public sealed class TestCurrentUser(
    Guid? userId,
    bool isAuthenticated = true,
    Guid? partyId = null,
    Guid? businessUnitId = null) : ICurrentUser
{
    public Guid? UserId { get; } = userId;

    public bool IsAuthenticated { get; } = isAuthenticated;

    public string? Oid => null;

    public string? Subject => null;

    public Guid? PartyId { get; } = partyId;

    public Guid? BusinessUnitId { get; } = businessUnitId;
}

/// <summary>Minimal <see cref="IIdGenerator"/> for tests: real UUIDv7s, just not wired through DI.</summary>
public sealed class TestIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7(TimeProvider.System.GetUtcNow());
}
