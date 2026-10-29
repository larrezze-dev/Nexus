using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Locations;
using Nexus.Platform.Domain.Locations;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Locations;

internal sealed class LocationRepository : ILocationRepository
{
    private readonly OrgDbContext _db;

    public LocationRepository(OrgDbContext db)
    {
        _db = db;
    }

    public void Add(Location location) => _db.Locations.Add(location);

    public Task<Location?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Locations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public RowMetadata GetMetadata(Location location) => OrgRowMetadata.Read(_db.Entry(location));
}
