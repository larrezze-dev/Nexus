using System.Text.Json;
using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Currencies;

namespace Nexus.Platform.Infrastructure.Currencies;

/// <summary>
/// <see cref="ICurrencyReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1). <c>caption_i18n</c> is cast to <c>text</c> in
/// SQL and deserialized here (same shape <c>I18nTextMapping</c> uses for EF Core) instead of
/// relying on Npgsql's dynamic JSON, which Dapper's plain connection does not have enabled.
/// </summary>
internal sealed class CurrencyReadDb : ICurrencyReadDb
{
    private const string DetailColumns =
        """
        code AS Code, caption_i18n::text AS CaptionI18nJson, minor_units AS MinorUnits,
        is_active AS IsActive, xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [CurrencySortFields.Code] = "code",
        [CurrencySortFields.MinorUnits] = "minor_units",
        [CurrencySortFields.IsActive] = "is_active",
    };

    private readonly IUnitOfWork _unitOfWork;

    public CurrencyReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CurrencyDetails?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"SELECT {DetailColumns} FROM org.currency WHERE code = @code",
            new { code },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<CurrencyListItem>> ListAsync(
        int page,
        int pageSize,
        IReadOnlyList<SortField> sort,
        CancellationToken cancellationToken)
    {
        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            "SELECT count(*) FROM org.currency",
            null,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[CurrencySortFields.Code]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT code AS Code, caption_i18n::text AS CaptionI18nJson, minor_units AS MinorUnits, is_active AS IsActive
             FROM org.currency
             ORDER BY {orderBy}, code
             LIMIT @limit OFFSET @offset
             """,
            new { limit = pageSize, offset = (long)(page - 1) * pageSize },
            cancellationToken));

        var items = rows
            .Select(r => new CurrencyListItem(r.Code, Deserialize(r.CaptionI18nJson), r.MinorUnits, r.IsActive))
            .ToList();

        return new PagedResult<CurrencyListItem>(items, page, pageSize, total);
    }

    public Task<bool> IsReferencedByActiveEntityAsync(string code, CancellationToken cancellationToken) =>
        _unitOfWork.Connection.ExecuteScalarAsync<bool>(Command(
            """
            SELECT EXISTS (SELECT 1 FROM org.legal_entity WHERE currency_code = @code AND is_active)
                OR EXISTS (SELECT 1 FROM org."group" WHERE reporting_currency_code = @code)
            """,
            new { code },
            cancellationToken));

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    // Dapper row types: settable properties (lenient mapping).
    private sealed class DetailRow
    {
        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public short MinorUnits { get; set; }

        public bool IsActive { get; set; }

        public long Version { get; set; }

        public CurrencyDetails ToDetails() =>
            new(Code, Deserialize(CaptionI18nJson), MinorUnits, IsActive, new CurrencyVersion(checked((uint)Version)));
    }

    private sealed class ListRow
    {
        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public short MinorUnits { get; set; }

        public bool IsActive { get; set; }
    }
}
