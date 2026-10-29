using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nexus.BuildingBlocks.Application;

namespace Nexus.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// Stamps the standard audit columns (<see cref="AuditColumns"/>) right before EF Core writes:
/// added rows get <c>created_*</c> and <c>updated_*</c>; modified rows get <c>updated_*</c> only
/// (<c>created_*</c> is marked unmodified, so it is never rewritten). Entities without the
/// shadow properties are ignored.
/// <list type="bullet">
///   <item>Actor: <see cref="ICurrentUser.UserId"/>. Saving only happens inside an active unit
///   of work, which already refused to begin without one (BE-R1), so a missing id here is a
///   wiring bug and throws.</item>
///   <item>Instant: <see cref="TimeProvider"/> (never <c>DateTime.UtcNow</c>), truncated to whole
///   microseconds, the precision of <c>timestamptz</c>, so the value kept in memory is exactly the
///   one stored.</item>
/// </list>
/// Scoped (it depends on the scoped <see cref="ICurrentUser"/>); a DbContext adds it in
/// <see cref="DbContextBase.ConfigureOptions"/>.
/// </summary>
public sealed class AuditStampingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuditStampingInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified
                        && e.Metadata.FindProperty(AuditColumns.CreatedAt) is not null)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var actor = _currentUser.UserId is { } id && id != Guid.Empty
            ? id
            : throw new InvalidOperationException("Cannot stamp audit columns without ICurrentUser.UserId (BE-R1).");

        var now = _timeProvider.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                Set(entry, AuditColumns.CreatedAt, now);
                Set(entry, AuditColumns.CreatedBy, actor);
            }
            else
            {
                entry.Property(AuditColumns.CreatedAt).IsModified = false;
                entry.Property(AuditColumns.CreatedBy).IsModified = false;
            }

            Set(entry, AuditColumns.UpdatedAt, now);
            Set(entry, AuditColumns.UpdatedBy, actor);
        }
    }

    private static void Set<T>(EntityEntry entry, string property, T value) =>
        entry.Property(property).CurrentValue = value;
}
