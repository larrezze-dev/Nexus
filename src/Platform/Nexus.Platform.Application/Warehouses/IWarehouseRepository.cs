using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Warehouses;

namespace Nexus.Platform.Application.Warehouses;

/// <summary>Write-side access to <see cref="Warehouse"/> aggregates (implemented over <c>OrgDbContext</c>).</summary>
public interface IWarehouseRepository
{
    /// <summary>Tracks a new warehouse; it is inserted on commit.</summary>
    void Add(Warehouse warehouse);

    /// <summary>Loads a warehouse for modification, or <see langword="null"/>.</summary>
    Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Concurrency version and audit instants of a tracked warehouse.</summary>
    RowMetadata GetMetadata(Warehouse warehouse);
}
