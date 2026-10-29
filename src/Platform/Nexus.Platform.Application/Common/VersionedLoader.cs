using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Common;

/// <summary>
/// Loads a single-row aggregate for modification and enforces optimistic concurrency (BE-R3):
/// <paramref name="notFound"/> if it does not exist, <paramref name="staleVersion"/> if the
/// caller's <c>If-Match</c> version (<paramref name="expectedVersion"/>, the row's <c>xmin</c>)
/// no longer matches. Shared by the BusinessUnit/Warehouse/Location use cases, whose aggregates
/// (unlike <c>AppUser</c>) span a single row, so there is no second row's version to combine.
/// </summary>
internal static class VersionedLoader
{
    public static async Task<Result<TEntity>> LoadAsync<TEntity>(
        Func<Guid, CancellationToken, Task<TEntity?>> getById,
        Func<TEntity, RowMetadata> getMetadata,
        Guid id,
        uint expectedVersion,
        Error notFound,
        Error staleVersion,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await getById(id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<TEntity>(notFound);
        }

        if (getMetadata(entity).Version != expectedVersion)
        {
            return Result.Failure<TEntity>(staleVersion);
        }

        return Result.Success(entity);
    }
}
