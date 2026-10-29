using Dapper;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Currencies;

namespace Nexus.Platform.Infrastructure.Currencies;

/// <summary>
/// <see cref="ICurrencyReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1).
/// </summary>
internal sealed class CurrencyReadDb : ICurrencyReadDb
{
    private readonly IUnitOfWork _unitOfWork;

    public CurrencyReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> IsActiveAsync(string code, CancellationToken cancellationToken)
    {
        var isActive = await _unitOfWork.Connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            "SELECT is_active FROM org.currency WHERE code = @code",
            new { code },
            _unitOfWork.Transaction,
            cancellationToken: cancellationToken));

        return isActive ?? false;
    }
}
