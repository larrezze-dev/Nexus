using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.RoleAssignments;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Application.RoleAssignments;

/// <summary>
/// Rule ACC-R12 (domain/access.md §6): at least one active user other than SYSTEM always holds a
/// currently valid <c>GLOBAL</c> assignment of <c>SYSTEM_ADMIN</c> (a "qualifying administrator").
/// Counted per user: a user with two such assignments is one administrator. SYSTEM never counts
/// (ORG-R6: it never signs in). Two operations can shrink that set, and both ask this guard
/// right before changing anything:
/// <list type="bullet">
///   <item><c>revokeRoleAssignment</c>, after ACC-R5/ACC-R6 and only when the revocation would
///   actually change something (<see cref="EnsureRevocationKeepsOneAsync"/>).</item>
///   <item><c>deactivateUser</c>, after ORG-R6/ACC-R13 and only when the user is still active
///   (<see cref="EnsureDeactivationKeepsOneAsync"/>).</item>
/// </list>
/// <para>
/// <b>Concurrency.</b> Two such operations on different administrators could each count two,
/// each remove one, and leave zero. So each takes one and the same lock inside its transaction
/// before counting - <c>FOR NO KEY UPDATE</c> on the <c>SYSTEM_ADMIN</c> row of
/// <c>access.role</c> (<see cref="ISystemAdministratorReadDb.LockRoleAsync"/>) - and counts with
/// a statement issued after it got the lock, which under READ COMMITTED sees what the previous
/// holder committed. They therefore run one after the other, and the second one counts the set
/// as the first one left it. The lock is held until commit or rollback. It is the only lock
/// either use case takes explicitly, and always the last before the writes, so it cannot form a
/// lock-ordering cycle with another of these operations. <c>FOR NO KEY UPDATE</c> does not block
/// the <c>FOR KEY SHARE</c> that inserting a <c>role_assignment</c> takes on <c>access.role</c>
/// (granting only grows the set), and it does not change the row's <c>xmin</c> (the role's
/// <c>ETag</c>).
/// </para>
/// <para>
/// The rejection is "this operation would take the set from at least one to zero". If the set is
/// already empty (for example every administrator's grant has expired), an operation that does
/// not touch a qualifying administrator is not blocked: it would not make anything worse.
/// </para>
/// </summary>
internal static class LastSystemAdministratorGuard
{
    public static readonly Error LastAdministrator = new(
        "ACC-R12",
        "At least one active user other than SYSTEM must keep a valid GLOBAL SYSTEM_ADMIN assignment; " +
        "this would remove the last one.");

    /// <summary>
    /// ACC-R12 for a revocation that <see cref="RoleAssignment.CheckRevocable"/> already allowed
    /// and that would change something. Lock-free exit (no lock, no count) unless the assignment
    /// is a <c>GLOBAL</c> <c>SYSTEM_ADMIN</c> one that is valid at <paramref name="now"/> and
    /// held by someone other than SYSTEM: <c>role_id</c>, <c>scope_type</c>, <c>user_id</c> and
    /// <c>valid_from</c> never change, and a concurrent revocation of the same row is caught by
    /// its <c>xmin</c> on save (409). Whether its user is still active is part of the locked
    /// count.
    /// </summary>
    public static async Task<Result> EnsureRevocationKeepsOneAsync(
        ISystemAdministratorReadDb readDb, RoleAssignment assignment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (assignment.ScopeType != RoleAssignment.ScopeGlobal
            || assignment.UserId == AppUser.SystemUserId
            || !assignment.IsValidAt(now))
        {
            return Result.Success();
        }

        if (await readDb.FindRoleIdAsync(cancellationToken) != assignment.RoleId)
        {
            return Result.Success();
        }

        var roleId = await readDb.LockRoleAsync(cancellationToken);
        if (roleId is null)
        {
            return Result.Success();
        }

        var before = await readDb.ListQualifyingGrantsAsync(roleId.Value, now, cancellationToken);
        return KeepsOne(before, before.Where(g => g.AssignmentId != assignment.Id));
    }

    /// <summary>
    /// ACC-R12 for deactivating the active, non-SYSTEM user <paramref name="userId"/> (ORG-R6,
    /// ACC-R13 and the idempotent no-op already handled by the caller). Always takes the lock:
    /// whether the user is a qualifying administrator can only be decided under it, because a
    /// <c>SYSTEM_ADMIN</c> <c>GLOBAL</c> grant to them committed after an unlocked look would
    /// otherwise go unnoticed while another administrator is being revoked. Deactivations are
    /// rare, and the lock is held only for the rest of this short transaction.
    /// </summary>
    public static async Task<Result> EnsureDeactivationKeepsOneAsync(
        ISystemAdministratorReadDb readDb, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var roleId = await readDb.LockRoleAsync(cancellationToken);
        if (roleId is null)
        {
            return Result.Success();
        }

        var before = await readDb.ListQualifyingGrantsAsync(roleId.Value, now, cancellationToken);
        return KeepsOne(before, before.Where(g => g.UserId != userId));
    }

    private static Result KeepsOne(IReadOnlyList<SystemAdministratorGrant> before, IEnumerable<SystemAdministratorGrant> after) =>
        before.Count > 0 && !after.Any()
            ? Result.Failure(LastAdministrator)
            : Result.Success();
}
