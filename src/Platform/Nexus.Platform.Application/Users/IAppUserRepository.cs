using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Application.Users;

/// <summary>Write-side access to <see cref="AppUser"/> aggregates (implemented over <c>OrgDbContext</c>).</summary>
public interface IAppUserRepository
{
    /// <summary>Tracks a new user; it is inserted on commit.</summary>
    void Add(AppUser user);

    /// <summary>Loads a user for modification, or <see langword="null"/>.</summary>
    Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked user.</summary>
    RowMetadata GetMetadata(AppUser user);
}
