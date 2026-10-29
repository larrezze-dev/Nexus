using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// Access to the <c>xmin</c> concurrency token that <see cref="DbContextBase"/> maps on every
/// root entity (BE-R3) as a shadow <see cref="uint"/> property. Repositories use it to expose a
/// row's version (the API's <c>ETag</c>, conventions-api.md §5) without leaking EF Core.
/// </summary>
public static class RowVersion
{
    /// <summary>Name of the shadow property (and of the PostgreSQL system column).</summary>
    public const string PropertyName = XminConcurrencyTokenConvention.PropertyName;

    /// <summary>
    /// The current <c>xmin</c> of a tracked entity: as loaded, or as returned by the last
    /// INSERT/UPDATE (EF Core reads it back with <c>RETURNING</c>).
    /// </summary>
    public static uint Read(EntityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (uint)entry.Property(PropertyName).CurrentValue!;
    }
}
