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

    /// <summary>
    /// Loads a user for modification after taking its row lock (<c>FOR NO KEY UPDATE</c>) in the
    /// current transaction, or <see langword="null"/>. Concurrent callers of this method on the
    /// same user queue behind each other and each one sees the row as the previous one committed
    /// it, so a write without <c>If-Match</c> never fails on a stale concurrency token
    /// (<c>PATCH /v1/me</c>, last write wins). The lock is held until commit/rollback.
    /// </summary>
    Task<AppUser?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked user.</summary>
    RowMetadata GetMetadata(AppUser user);
}
