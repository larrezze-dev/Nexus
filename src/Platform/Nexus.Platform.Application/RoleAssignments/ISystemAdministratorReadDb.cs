namespace Nexus.Platform.Application.RoleAssignments;

/// <summary>
/// The reads ACC-R12 needs (<see cref="LastSystemAdministratorGuard"/>), on the unit of work's own
/// connection and transaction. Implemented in infrastructure.
/// </summary>
public interface ISystemAdministratorReadDb
{
    /// <summary>
    /// The id of the <c>SYSTEM_ADMIN</c> role (by code), without locking anything;
    /// <see langword="null"/> if it does not exist.
    /// </summary>
    Task<Guid?> FindRoleIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The ACC-R12 lock: <c>SELECT … FROM access.role WHERE code = 'SYSTEM_ADMIN' FOR NO KEY
    /// UPDATE</c>, held until the transaction ends. Every operation that can shrink the set of
    /// qualifying administrators takes it before counting, so they run one after the other.
    /// Returns the role id (<see langword="null"/> if the role does not exist: nothing to protect).
    /// </summary>
    Task<Guid?> LockRoleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Every assignment that makes its user a qualifying administrator at <paramref name="now"/>
    /// (ACC-R12): role <paramref name="systemAdministratorRoleId"/> (active), <c>GLOBAL</c>,
    /// not revoked, <c>valid_from &lt;= now &lt; coalesce(valid_to, infinity)</c>, held by an active
    /// user other than SYSTEM. A fresh statement: under READ COMMITTED it sees everything
    /// committed before it started, in particular by whoever held the lock before this transaction.
    /// </summary>
    Task<IReadOnlyList<SystemAdministratorGrant>> ListQualifyingGrantsAsync(
        Guid systemAdministratorRoleId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>One qualifying <c>SYSTEM_ADMIN</c> <c>GLOBAL</c> assignment (ACC-R12).</summary>
public sealed record SystemAdministratorGrant(Guid AssignmentId, Guid UserId);
