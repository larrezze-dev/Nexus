using System.Globalization;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using Testcontainers.ClickHouse;

namespace Nexus.Projections.Tests.Runtime.Infrastructure;

/// <summary>A throwaway ClickHouse (same image as compose) with small query helpers.</summary>
public sealed class ClickHouseFixture : IAsyncLifetime
{
    public const string Image = "clickhouse/clickhouse-server:26.8.34.13";

    private readonly ClickHouseContainer _container = new ClickHouseBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task ExecuteAsync(string sql)
    {
        using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteStatementAsync(sql);
    }

    public async Task<long> CountAsync(string sql)
    {
        using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();
        return Convert.ToInt64(await connection.ExecuteScalarAsync(sql), CultureInfo.InvariantCulture);
    }

    /// <summary>Every row of <paramref name="sql"/>, each value rendered with the invariant culture (for set comparison).</summary>
    public async Task<List<string>> RowsAsync(string sql)
    {
        using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();
        using var reader = await connection.ExecuteReaderAsync(sql);
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = Render(reader.GetValue(i));
            }

            rows.Add(string.Join(" | ", values));
        }

        return rows;
    }

    private static string Render(object? value) => value switch
    {
        null or DBNull => "NULL",
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        System.Collections.IDictionary map => string.Join(",", map.Keys.Cast<object>().Select(k => $"{k}={map[k]}").Order(StringComparer.Ordinal)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
