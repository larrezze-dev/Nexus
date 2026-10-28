namespace Nexus.BuildingBlocks.Application.Paging;

/// <summary>One field of a <c>sort</c> parameter.</summary>
public sealed record SortField(string Field, bool Descending);

/// <summary>
/// Parses the <c>sort</c> query parameter (conventions-api.md §4: comma-separated API field
/// names, <c>-</c> prefix for descending, e.g. <c>code,-updatedAt</c>) against the fields a
/// listing allows. Field names are the API's camelCase names, never column names: the read
/// side maps each allowed field to its SQL expression from a fixed whitelist, so nothing from
/// the request is ever concatenated into SQL.
/// </summary>
public static class SortSpec
{
    /// <summary>
    /// Parses <paramref name="sort"/>. Empty/<see langword="null"/> yields an empty list (the
    /// listing's default order). Returns <see langword="false"/> with a message for an unknown
    /// or repeated field, or an empty segment.
    /// </summary>
    public static bool TryParse(
        string? sort,
        IReadOnlySet<string> allowedFields,
        out IReadOnlyList<SortField> fields,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(allowedFields);

        var parsed = new List<SortField>();
        fields = parsed;
        error = null;

        if (string.IsNullOrWhiteSpace(sort))
        {
            return true;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in sort.Split(','))
        {
            var segment = raw.Trim();
            var descending = segment.StartsWith('-');
            var name = descending ? segment[1..] : segment;

            if (name.Length == 0)
            {
                error = "sort: empty field name.";
                return false;
            }

            if (!allowedFields.Contains(name))
            {
                error = $"sort: unknown field '{name}'. Allowed: {string.Join(", ", allowedFields.Order(StringComparer.Ordinal))}.";
                return false;
            }

            if (!seen.Add(name))
            {
                error = $"sort: field '{name}' appears more than once.";
                return false;
            }

            parsed.Add(new SortField(name, descending));
        }

        return true;
    }
}
