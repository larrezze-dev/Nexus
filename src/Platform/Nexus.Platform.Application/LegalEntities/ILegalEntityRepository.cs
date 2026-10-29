using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.LegalEntities;

namespace Nexus.Platform.Application.LegalEntities;

/// <summary>Write-side access to <see cref="LegalEntity"/> aggregates (implemented over <c>OrgDbContext</c>).</summary>
public interface ILegalEntityRepository
{
    /// <summary>Tracks a new legal entity; it is inserted on commit.</summary>
    void Add(LegalEntity legalEntity);

    /// <summary>Loads a legal entity for modification, or <see langword="null"/>.</summary>
    Task<LegalEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked legal entity.</summary>
    RowMetadata GetMetadata(LegalEntity legalEntity);
}
