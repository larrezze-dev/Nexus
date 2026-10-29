using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.BusinessUnits;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.BusinessUnits;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.BusinessUnits;

internal sealed class BusinessUnitRepository : IBusinessUnitRepository
{
    private readonly OrgDbContext _db;

    public BusinessUnitRepository(OrgDbContext db)
    {
        _db = db;
    }

    public void Add(BusinessUnit businessUnit) => _db.BusinessUnits.Add(businessUnit);

    public Task<BusinessUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.BusinessUnits.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

    public RowMetadata GetMetadata(BusinessUnit businessUnit) => OrgRowMetadata.Read(_db.Entry(businessUnit));
}
