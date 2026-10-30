using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Locations;

namespace Nexus.Platform.Infrastructure.Locations;

/// <summary>
/// <see cref="ILocationReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1).
/// </summary>
internal sealed class LocationReadDb : ILocationReadDb
{
    private const string DetailColumns =
        """
        l.id AS Id, l.code AS Code, l.name AS Name, l.street AS Street, l.postal_code AS PostalCode,
        l.city AS City, l.region AS Region, l.country_code AS CountryCode, l.is_active AS IsActive,
        l.created_at AS CreatedAt, l.updated_at AS UpdatedAt, l.xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [LocationSortFields.Name] = "l.name",
        [LocationSortFields.City] = "l.city",
        [LocationSortFields.IsActive] = "l.is_active",
        [LocationSortFields.CreatedAt] = "l.created_at",
        [LocationSortFields.UpdatedAt] = "l.updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public LocationReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<LocationDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"SELECT {DetailColumns} FROM org.location l WHERE l.id = @id",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<LocationListItem>> ListAsync(
        int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken)
    {
        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            "SELECT count(*) FROM org.location",
            null,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[LocationSortFields.Name]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT l.id AS Id, l.code AS Code, l.name AS Name, l.city AS City,
                    l.country_code AS CountryCode, l.is_active AS IsActive
             FROM org.location l
             ORDER BY {orderBy}, l.id
             LIMIT @limit OFFSET @offset
             """,
            new { limit = pageSize, offset = (long)(page - 1) * pageSize },
            cancellationToken));

        var items = rows
            .Select(r => new LocationListItem(r.Id, r.Code, r.Name, r.City, r.CountryCode, r.IsActive))
            .ToList();

        return new PagedResult<LocationListItem>(items, page, pageSize, total);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public string? Code { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Street { get; set; }

        public string? PostalCode { get; set; }

        public string? City { get; set; }

        public string? Region { get; set; }

        public string CountryCode { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long Version { get; set; }

        public LocationDetails ToDetails() => new(
            Id, Code, Name, Street, PostalCode, City, Region, CountryCode, IsActive,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            checked((uint)Version));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public string? Code { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? City { get; set; }

        public string CountryCode { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
