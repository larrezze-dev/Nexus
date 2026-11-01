using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Application.Roles;

/// <summary>Write-side access to <see cref="Role"/> aggregates (implemented over <c>AccessDbContext</c>).</summary>
public interface IRoleRepository
{
    /// <summary>Tracks a new role; it is inserted on commit.</summary>
    void Add(Role role);

    /// <summary>Loads a role for modification, or <see langword="null"/>.</summary>
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked role.</summary>
    RowMetadata GetMetadata(Role role);
}
