using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Infrastructure.Users;

/// <summary>
/// <see cref="IUserReadDb"/> with Dapper on the unit of work's own connection and transaction
/// (conventions-backend.md §1, BE-R1). Every value is a parameter; the only SQL fragments that
/// vary are ORDER BY expressions taken from the fixed <see cref="SortColumns"/> whitelist.
/// </summary>
internal sealed class UserReadDb : IUserReadDb
{
    private const string DetailColumns =
        """
        u.id AS Id, u.party_id AS PartyId, u.external_subject AS Oid, p.legal_name AS FullName,
        u.email AS Email, u.preferred_language AS PreferredLanguage, u.business_unit_id AS BusinessUnitId,
        u.is_active AS IsActive, u.created_at AS CreatedAt,
        GREATEST(u.updated_at, p.updated_at) AS UpdatedAt,
        u.xmin::text::bigint AS UserVersion, p.xmin::text::bigint AS PartyVersion
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [UserSortFields.FullName] = "p.legal_name",
        [UserSortFields.Email] = "u.email",
        [UserSortFields.PreferredLanguage] = "u.preferred_language",
        [UserSortFields.IsActive] = "u.is_active",
        [UserSortFields.CreatedAt] = "u.created_at",
        [UserSortFields.UpdatedAt] = "GREATEST(u.updated_at, p.updated_at)",
    };

    private readonly IUnitOfWork _unitOfWork;

    public UserReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<UserDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"SELECT {DetailColumns} FROM org.app_user u JOIN org.party p ON p.id = u.party_id WHERE u.id = @id",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<UserListItem>> ListAsync(
        int page,
        int pageSize,
        IReadOnlyList<SortField> sort,
        CancellationToken cancellationToken)
    {
        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            "SELECT count(*) FROM org.app_user",
            null,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[UserSortFields.FullName]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT u.id AS Id, u.external_subject AS Oid, p.legal_name AS FullName, u.email AS Email,
                    u.preferred_language AS PreferredLanguage, u.business_unit_id AS BusinessUnitId, u.is_active AS IsActive
             FROM org.app_user u JOIN org.party p ON p.id = u.party_id
             ORDER BY {orderBy}, u.id
             LIMIT @limit OFFSET @offset
             """,
            new { limit = pageSize, offset = (long)(page - 1) * pageSize },
            cancellationToken));

        var items = rows
            .Select(r => new UserListItem(r.Id, r.Oid, r.FullName, r.Email, r.PreferredLanguage, r.BusinessUnitId, r.IsActive))
            .ToList();

        return new PagedResult<UserListItem>(items, page, pageSize, total);
    }

    public async Task<CurrentUserProfile?> GetActiveProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<ProfileRow>(Command(
            """
            SELECT id AS UserId, party_id AS PartyId, business_unit_id AS BusinessUnitId, preferred_language AS PreferredLanguage
            FROM org.app_user WHERE id = @userId AND is_active
            """,
            new { userId },
            cancellationToken));

        return row is null ? null : new CurrentUserProfile(row.UserId, row.PartyId, row.BusinessUnitId, row.PreferredLanguage);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    // Dapper row types: settable properties (lenient mapping), UTC DateTime for timestamptz.
    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public Guid PartyId { get; set; }

        public string? Oid { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string PreferredLanguage { get; set; } = string.Empty;

        public Guid? BusinessUnitId { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long UserVersion { get; set; }

        public long PartyVersion { get; set; }

        public UserDetails ToDetails() => new(
            Id, PartyId, Oid, FullName, Email, PreferredLanguage, BusinessUnitId, IsActive,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            new UserVersion(checked((uint)UserVersion), checked((uint)PartyVersion)));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public string? Oid { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string PreferredLanguage { get; set; } = string.Empty;

        public Guid? BusinessUnitId { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class ProfileRow
    {
        public Guid UserId { get; set; }

        public Guid PartyId { get; set; }

        public Guid? BusinessUnitId { get; set; }

        public string PreferredLanguage { get; set; } = string.Empty;
    }
}
