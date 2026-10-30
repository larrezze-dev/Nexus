using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Locations;

namespace Nexus.Platform.Application.Locations;

/// <summary>Write-side access to <see cref="Location"/> aggregates (implemented over <c>OrgDbContext</c>).</summary>
public interface ILocationRepository
{
    /// <summary>Tracks a new location; it is inserted on commit.</summary>
    void Add(Location location);

    /// <summary>Loads a location for modification, or <see langword="null"/>.</summary>
    Task<Location?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked location.</summary>
    RowMetadata GetMetadata(Location location);
}
