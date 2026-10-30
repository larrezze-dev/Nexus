using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Currencies;

namespace Nexus.Platform.Application.Currencies;

/// <summary>Detail read model of a currency (<c>Currency</c> schema of
/// <c>contracts/openapi/platform.yaml</c>) plus its <see cref="Version"/> for the <c>ETag</c>.</summary>
public sealed record CurrencyDetails(
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    short MinorUnits,
    bool IsActive,
    CurrencyVersion Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static CurrencyDetails From(Currency currency, RowMetadata row)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return new CurrencyDetails(currency.Id, currency.CaptionI18n, currency.MinorUnits, currency.IsActive, new CurrencyVersion(row.Version));
    }
}

/// <summary>List read model of a currency (<c>CurrencyListItem</c> schema).</summary>
public sealed record CurrencyListItem(
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    short MinorUnits,
    bool IsActive);
