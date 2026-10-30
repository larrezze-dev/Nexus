using System.Globalization;

namespace Nexus.Platform.Application.Currencies;

/// <summary>
/// The optimistic-concurrency version of the Currency resource (BE-R3; conventions-api.md §5,
/// <c>ETag: "&lt;xmin&gt;"</c>). A Currency is a single row, so this is just its <c>xmin</c>
/// rendered as the opaque token the ETag carries.
/// </summary>
public readonly record struct CurrencyVersion(uint Value)
{
    /// <summary>The token, without the ETag quotes (those are HTTP's business).</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses a token produced by <see cref="ToString"/>.</summary>
    public static bool TryParse(string? token, out CurrencyVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (!uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        version = new CurrencyVersion(value);
        return true;
    }
}
