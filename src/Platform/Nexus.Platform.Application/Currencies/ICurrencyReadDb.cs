namespace Nexus.Platform.Application.Currencies;

/// <summary>
/// Read-only lookup of <c>org.currency</c> used to enforce Rule ORG-R7
/// (<c>legal_entity.currency_code</c>/<c>group.reporting_currency_code</c> must reference an
/// active currency) before a <c>Group</c>/<c>LegalEntity</c> create or update is saved.
/// Requires an active transaction (conventions-backend.md §1, BE-R1).
/// </summary>
public interface ICurrencyReadDb
{
    /// <summary><see langword="true"/> only if <paramref name="code"/> exists and is active.</summary>
    Task<bool> IsActiveAsync(string code, CancellationToken cancellationToken);
}
