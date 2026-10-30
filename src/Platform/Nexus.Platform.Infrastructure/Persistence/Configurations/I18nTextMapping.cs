using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps a translatable text (<c>caption_i18n jsonb</c>, conventions-api.md §5: an object with one
/// key per language, <c>de</c> mandatory, enforced by the column's CHECK) to an
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/>. Stored as a JSON string with an explicit
/// converter rather than Npgsql's dynamic JSON (which would require <c>EnableDynamicJson</c> on
/// the shared data source); the value comparer compares contents, so replacing the dictionary
/// with an equal one is not a change.
/// </summary>
internal static class I18nTextMapping
{
    public static PropertyBuilder<IReadOnlyDictionary<string, string>> IsI18nText(
        this PropertyBuilder<IReadOnlyDictionary<string, string>> property)
    {
        property
            .HasColumnType("jsonb")
            .HasConversion(
                value => Serialize(value),
                json => Deserialize(json),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (left, right) => AreEqual(left, right),
                    value => HashOf(value),
                    value => Copy(value)));

        return property;
    }

    private static string Serialize(IReadOnlyDictionary<string, string> value) =>
        JsonSerializer.Serialize(value);

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    private static bool AreEqual(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right) =>
        ReferenceEquals(left, right)
        || (left is not null && right is not null && left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var other) && string.Equals(pair.Value, other, StringComparison.Ordinal)));

    private static int HashOf(IReadOnlyDictionary<string, string> value) =>
        value.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value));

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> value) =>
        new Dictionary<string, string>(value);
}
