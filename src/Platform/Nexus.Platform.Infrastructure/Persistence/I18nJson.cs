using System.Text.Json;

namespace Nexus.Platform.Infrastructure.Persistence;

/// <summary>
/// Parses a <c>caption_i18n jsonb</c> column (read as text by Dapper/Npgsql) into the same
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> shape <see cref="Configurations.I18nTextMapping"/>
/// maps for EF Core. Shared by the Dapper read sides that expose a <c>captionI18n</c> field
/// (<c>BusinessUnitReadDb</c>, <c>WarehouseReadDb</c>).
/// </summary>
internal static class I18nJson
{
    public static IReadOnlyDictionary<string, string> Parse(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
