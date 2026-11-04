using System.Text;
using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.RoleAssignments;
using Nexus.Platform.Domain.RoleAssignments;

namespace Nexus.Platform.Infrastructure.RoleAssignments;

/// <summary>
/// <see cref="IRoleAssignmentReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1). The <see cref="RoleAssignmentFilter"/>
/// clauses are appended to a fixed <c>WHERE</c> skeleton; every filter value is a parameter,
/// never concatenated into the SQL text.
/// </summary>
internal sealed class RoleAssignmentReadDb : IRoleAssignmentReadDb
{
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [RoleAssignmentSortFields.ValidFrom] = "ra.valid_from",
        [RoleAssignmentSortFields.CreatedAt] = "ra.created_at",
        [RoleAssignmentSortFields.UpdatedAt] = "ra.updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public RoleAssignmentReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<RoleAssignmentListItem>> ListAllAsync(
        IReadOnlyList<SortField> sort, RoleAssignmentFilter filter, CancellationToken cancellationToken)
    {
        var (where, parameters) = BuildWhere(filter);

        var orderBy = sort.Count == 0
            ? SortColumns[RoleAssignmentSortFields.ValidFrom]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        // No LIMIT/OFFSET: the handler filters by scope coverage first, then paginates
        // (see IRoleAssignmentReadDb.ListAllAsync).
        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT ra.id AS Id, ra.user_id AS UserId, ra.role_id AS RoleId, ra.scope_type AS ScopeType,
                    ra.scope_entity_id AS ScopeEntityId, ra.valid_from AS ValidFrom, ra.valid_to AS ValidTo,
                    ra.source AS Source
             FROM access.role_assignment ra
             {where}
             ORDER BY {orderBy}, ra.id
             """,
            parameters,
            cancellationToken));

        return rows
            .Select(r => new RoleAssignmentListItem(r.Id, r.UserId, r.RoleId, r.ScopeType, r.ScopeEntityId, r.ValidFrom, r.ValidTo, r.Source))
            .ToList();
    }

    public async Task<RoleAssignmentDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // Version = xmin, the same concurrency token the repository (EF) reads, so this ETag is
        // the one revokeRoleAssignment's If-Match is compared against.
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            """
            SELECT ra.id AS Id, ra.user_id AS UserId, ra.role_id AS RoleId, ra.scope_type AS ScopeType,
                   ra.scope_entity_id AS ScopeEntityId, ra.valid_from AS ValidFrom, ra.valid_to AS ValidTo,
                   ra.granted_by AS GrantedBy, ra.source AS Source, ra.source_ref_id AS SourceRefId,
                   ra.revoked_by AS RevokedBy, ra.created_at AS CreatedAt, ra.updated_at AS UpdatedAt,
                   ra.xmin::text::bigint AS Version
            FROM access.role_assignment ra
            WHERE ra.id = @id
            """,
            new { id },
            cancellationToken));

        return row is null
            ? null
            : new RoleAssignmentDetails(
                row.Id, row.UserId, row.RoleId, row.ScopeType, row.ScopeEntityId, row.ValidFrom, row.ValidTo,
                row.GrantedBy, row.Source, row.SourceRefId, row.RevokedBy, row.CreatedAt, row.UpdatedAt,
                checked((uint)row.Version));
    }

    private static (string Where, DynamicParameters Parameters) BuildWhere(RoleAssignmentFilter filter)
    {
        var clauses = new List<string>
        {
            "ra.valid_from <= @activeOn AND (ra.valid_to IS NULL OR ra.valid_to > @activeOn)",
        };
        var parameters = new DynamicParameters();
        parameters.Add("activeOn", filter.ActiveOn);

        if (filter.UserId is { } userId)
        {
            clauses.Add("ra.user_id = @userId");
            parameters.Add("userId", userId);
        }

        if (filter.RoleId is { } roleId)
        {
            clauses.Add("ra.role_id = @roleId");
            parameters.Add("roleId", roleId);
        }

        if (!string.IsNullOrWhiteSpace(filter.ScopeType))
        {
            clauses.Add("ra.scope_type = @scopeType");
            parameters.Add("scopeType", filter.ScopeType);
        }

        var where = new StringBuilder("WHERE ").Append(string.Join(" AND ", clauses));
        return (where.ToString(), parameters);
    }

    public async Task<bool> ScopeEntityExistsAsync(string scopeType, Guid scopeEntityId, CancellationToken cancellationToken)
    {
        // Fixed SQL per scope type (never concatenated from input); the id is a parameter.
        var sql = scopeType switch
        {
            RoleAssignment.ScopeGroup => "SELECT EXISTS (SELECT 1 FROM org.\"group\" WHERE id = @id)",
            RoleAssignment.ScopeLegalEntity => "SELECT EXISTS (SELECT 1 FROM org.legal_entity WHERE id = @id)",
            RoleAssignment.ScopeBusinessUnit => "SELECT EXISTS (SELECT 1 FROM org.business_unit WHERE id = @id)",
            RoleAssignment.ScopeWarehouse => "SELECT EXISTS (SELECT 1 FROM org.warehouse WHERE id = @id)",
            _ => null,
        };

        if (sql is null)
        {
            return false;
        }

        return await _unitOfWork.Connection.ExecuteScalarAsync<bool>(
            Command(sql, new { id = scopeEntityId }, cancellationToken));
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid RoleId { get; set; }

        public string ScopeType { get; set; } = string.Empty;

        public Guid? ScopeEntityId { get; set; }

        public DateTimeOffset ValidFrom { get; set; }

        public DateTimeOffset? ValidTo { get; set; }

        public Guid GrantedBy { get; set; }

        public string Source { get; set; } = string.Empty;

        public Guid? SourceRefId { get; set; }

        public Guid? RevokedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public long Version { get; set; }
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid RoleId { get; set; }

        public string ScopeType { get; set; } = string.Empty;

        public Guid? ScopeEntityId { get; set; }

        public DateTimeOffset ValidFrom { get; set; }

        public DateTimeOffset? ValidTo { get; set; }

        public string Source { get; set; } = string.Empty;
    }
}
