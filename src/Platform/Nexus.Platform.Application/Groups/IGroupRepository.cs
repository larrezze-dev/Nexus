using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Groups;

namespace Nexus.Platform.Application.Groups;

/// <summary>Write-side access to <see cref="Group"/> aggregates (implemented over <c>OrgDbContext</c>).</summary>
public interface IGroupRepository
{
    /// <summary>Tracks a new group; it is inserted on commit.</summary>
    void Add(Group group);

    /// <summary>Loads a group for modification, or <see langword="null"/>.</summary>
    Task<Group?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked group.</summary>
    RowMetadata GetMetadata(Group group);
}
