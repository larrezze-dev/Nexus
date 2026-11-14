using Nexus.Platform.Application.Currencies;

namespace Nexus.Platform.Api.Currencies;

// HTTP shapes of contracts/openapi/platform.yaml (Currencies). PascalCase here, camelCase on
// the wire (JsonSerializerDefaults.Web).

/// <summary><c>CreateCurrencyRequest</c>.</summary>
public sealed record CreateCurrencyRequest
{
    public required string Code { get; init; }

    public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }

    public required short MinorUnits { get; init; }
}

/// <summary><c>UpdateCurrencyRequest</c>.</summary>
public sealed record UpdateCurrencyRequest
{
    public required bool IsActive { get; init; }

    /// <summary>Optional: omitted = unchanged; present = replaces the whole map (<c>de</c> required).</summary>
    public IReadOnlyDictionary<string, string>? CaptionI18n { get; init; }
}

/// <summary><c>Currency</c>.</summary>
public sealed record CurrencyResponse(
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    short MinorUnits,
    bool IsActive)
{
    public static CurrencyResponse From(CurrencyDetails currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new CurrencyResponse(currency.Code, currency.CaptionI18n, currency.MinorUnits, currency.IsActive);
    }
}

/// <summary><c>CurrencyListItem</c>.</summary>
public sealed record CurrencyListItemResponse(
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    short MinorUnits,
    bool IsActive);
