using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.RoleAssignments;

namespace Nexus.Platform.Application.RoleAssignments;

/// <summary>Write-side access to <see cref="RoleAssignment"/> aggregates (implemented over <c>RoleAssignmentDbContext</c>).</summary>
public interface IRoleAssignmentRepository
{
    /// <summary>Tracks a new role assignment; it is inserted on commit.</summary>
    void Add(RoleAssignment assignment);

    /// <summary>Loads a role assignment for modification, or <see langword="null"/>.</summary>
    Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked role assignment.</summary>
    RowMetadata GetMetadata(RoleAssignment assignment);
}
