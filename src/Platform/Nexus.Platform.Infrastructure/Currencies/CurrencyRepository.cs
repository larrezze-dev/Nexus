using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Currencies;
using Nexus.Platform.Domain.Currencies;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Currencies;

internal sealed class CurrencyRepository : ICurrencyRepository
{
    private readonly OrgDbContext _db;

    public CurrencyRepository(OrgDbContext db)
    {
        _db = db;
    }

    public Task<Currency?> GetByIdAsync(string code, CancellationToken cancellationToken) =>
        _db.Currencies.SingleOrDefaultAsync(c => c.Id == code, cancellationToken);

    public RowMetadata GetMetadata(Currency currency) => OrgRowMetadata.Read(_db.Entry(currency));
}
