using Nexus.Platform.Application.Currencies;

namespace Nexus.Platform.Api.Currencies;

// HTTP shapes of contracts/openapi/platform.yaml (Currencies). PascalCase here, camelCase on
// the wire (JsonSerializerDefaults.Web).

/// <summary><c>UpdateCurrencyRequest</c>.</summary>
public sealed record UpdateCurrencyRequest
{
    public required bool IsActive { get; init; }
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
