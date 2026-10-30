using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.BusinessUnits;

namespace Nexus.Platform.Application.BusinessUnits;

/// <summary>Write-side access to <see cref="BusinessUnit"/> aggregates (implemented over <c>OrgDbContext</c>).</summary>
public interface IBusinessUnitRepository
{
    /// <summary>Tracks a new business unit; it is inserted on commit.</summary>
    void Add(BusinessUnit businessUnit);

    /// <summary>Loads a business unit for modification, or <see langword="null"/>.</summary>
    Task<BusinessUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked business unit.</summary>
    RowMetadata GetMetadata(BusinessUnit businessUnit);
}
