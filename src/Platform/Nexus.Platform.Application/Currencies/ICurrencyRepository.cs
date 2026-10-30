using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Currencies;

namespace Nexus.Platform.Application.Currencies;

/// <summary>
/// Write-side access to <see cref="Currency"/> aggregates (implemented over <c>OrgDbContext</c>).
/// No <c>Add</c>: currencies are seeded once by <c>db/seed/common</c>, never created through the
/// API.
/// </summary>
public interface ICurrencyRepository
{
    /// <summary>Loads a currency for modification, or <see langword="null"/>.</summary>
    Task<Currency?> GetByIdAsync(string code, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked currency.</summary>
    RowMetadata GetMetadata(Currency currency);
}
