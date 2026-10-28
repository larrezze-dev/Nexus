namespace Nexus.Platform.Application.Common;

/// <summary>
/// Persistence metadata of one loaded or saved aggregate row, read back by a repository
/// without exposing EF Core to handlers: the optimistic-concurrency <see cref="Version"/>
/// (<c>xmin</c>, BE-R3, the source of the <c>ETag</c>, conventions-api.md §5) and the audit
/// instants. After a commit they hold the values just written.
/// </summary>
public readonly record struct RowMetadata(uint Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
