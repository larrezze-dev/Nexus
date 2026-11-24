using Dapper;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.RoleAssignments;
using Nexus.Platform.Domain.RoleAssignments;
using Nexus.Platform.Domain.Roles;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Infrastructure.RoleAssignments;

/// <summary>
/// <see cref="ISystemAdministratorReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1), so the lock it takes lives exactly as long as
/// the use case's transaction. Reads only <c>access</c> and <c>org</c>, both owned by
/// <c>nexus-platform</c> (ARC-R1/ARC-R3).
/// </summary>
internal sealed class SystemAdministratorReadDb : ISystemAdministratorReadDb
{
    private readonly IUnitOfWork _unitOfWork;

    public SystemAdministratorReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Guid?> FindRoleIdAsync(CancellationToken cancellationToken) =>
        _unitOfWork.Connection.QuerySingleOrDefaultAsync<Guid?>(Command(
            "SELECT id FROM access.role WHERE code = @code",
            new { code = SystemAdministrator.RoleCode },
            cancellationToken));

    public Task<Guid?> LockRoleAsync(CancellationToken cancellationToken) =>
        // FOR NO KEY UPDATE, not FOR UPDATE: it conflicts with itself (what serializes ACC-R12)
        // but not with the FOR KEY SHARE a concurrent INSERT into access.role_assignment /
        // access.role_permission takes on this row through their FK. A single row, so no
        // ordering issue between two holders. Waits for the current holder, then holds the
        // lock until this transaction ends; the caller then counts with a NEW statement
        // (ListQualifyingGrantsAsync), which under READ COMMITTED sees what the previous holder
        // committed.
        _unitOfWork.Connection.QuerySingleOrDefaultAsync<Guid?>(Command(
            "SELECT id FROM access.role WHERE code = @code FOR NO KEY UPDATE",
            new { code = SystemAdministrator.RoleCode },
            cancellationToken));

    public async Task<IReadOnlyList<SystemAdministratorGrant>> ListQualifyingGrantsAsync(
        Guid systemAdministratorRoleId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Connection.QueryAsync<GrantRow>(Command(
            """
            SELECT ra.id AS AssignmentId, ra.user_id AS UserId
            FROM access.role_assignment ra
            JOIN access.role r ON r.id = ra.role_id
            JOIN org.app_user u ON u.id = ra.user_id
            WHERE ra.role_id = @roleId
              AND r.is_active
              AND ra.scope_type = @global
              AND ra.revoked_by IS NULL
              AND ra.valid_from <= @now
              AND (ra.valid_to IS NULL OR ra.valid_to > @now)
              AND u.is_active
              AND u.id <> @systemUserId
            """,
            new
            {
                roleId = systemAdministratorRoleId,
                global = RoleAssignment.ScopeGlobal,
                now,
                systemUserId = AppUser.SystemUserId,
            },
            cancellationToken));

        return rows.Select(r => new SystemAdministratorGrant(r.AssignmentId, r.UserId)).ToList();
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    private sealed class GrantRow
    {
        public Guid AssignmentId { get; set; }

        public Guid UserId { get; set; }
    }
}
