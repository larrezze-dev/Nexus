using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Currencies;

/// <summary>Rule ORG-R7, shared by every command that writes a currency code (Group, LegalEntity).</summary>
internal static class CurrencyRules
{
    public static readonly Error NotActive =
        new("ORG-R7", "reportingCurrencyCode/currencyCode must reference an active currency.");

    /// <summary>
    /// Fails with <see cref="NotActive"/> (ORG-R7) unless <paramref name="code"/> exists and is
    /// active in <c>org.currency</c>.
    /// </summary>
    public static async Task<Result> EnsureActiveAsync(ICurrencyReadDb currencies, string code, CancellationToken cancellationToken)
    {
        var isActive = await currencies.IsActiveAsync(code, cancellationToken);
        return isActive ? Result.Success() : Result.Failure(NotActive);
    }
}
