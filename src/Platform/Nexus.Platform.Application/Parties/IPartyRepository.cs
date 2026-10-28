using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.Parties;

/// <summary>Write-side access to <see cref="Party"/> aggregates (implemented over <c>OrgDbContext</c>).</summary>
public interface IPartyRepository
{
    /// <summary>Tracks a new party; it is inserted on commit.</summary>
    void Add(Party party);

    /// <summary>Loads a party for modification, or <see langword="null"/>.</summary>
    Task<Party?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked party.</summary>
    RowMetadata GetMetadata(Party party);
}
