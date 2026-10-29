using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Parties;

internal sealed class PartyRepository : IPartyRepository
{
    private readonly OrgDbContext _db;

    public PartyRepository(OrgDbContext db)
    {
        _db = db;
    }

    public void Add(Party party) => _db.Parties.Add(party);

    public Task<Party?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Parties.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public RowMetadata GetMetadata(Party party) => OrgRowMetadata.Read(_db.Entry(party));
}
