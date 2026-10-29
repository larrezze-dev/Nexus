using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Warehouses;
using Nexus.Platform.Domain.Warehouses;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Warehouses;

internal sealed class WarehouseRepository : IWarehouseRepository
{
    private readonly OrgDbContext _db;

    public WarehouseRepository(OrgDbContext db)
    {
        _db = db;
    }

    public void Add(Warehouse warehouse) => _db.Warehouses.Add(warehouse);

    public Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);

    public RowMetadata GetMetadata(Warehouse warehouse) => OrgRowMetadata.Read(_db.Entry(warehouse));
}
