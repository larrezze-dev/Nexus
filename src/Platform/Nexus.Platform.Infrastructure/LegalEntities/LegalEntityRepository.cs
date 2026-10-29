using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.LegalEntities;
using Nexus.Platform.Domain.LegalEntities;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.LegalEntities;

internal sealed class LegalEntityRepository : ILegalEntityRepository
{
    private readonly OrgDbContext _db;

    public LegalEntityRepository(OrgDbContext db)
    {
        _db = db;
    }

    public void Add(LegalEntity legalEntity) => _db.LegalEntities.Add(legalEntity);

    public Task<LegalEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.LegalEntities.SingleOrDefaultAsync(le => le.Id == id, cancellationToken);

    public RowMetadata GetMetadata(LegalEntity legalEntity) => OrgRowMetadata.Read(_db.Entry(legalEntity));
}
