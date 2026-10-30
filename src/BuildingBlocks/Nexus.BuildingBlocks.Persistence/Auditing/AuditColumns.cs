using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// The standard audit columns of every mutable table (conventions-database.md §4:
/// <c>created_at</c>, <c>created_by</c>, <c>updated_at</c>, <c>updated_by</c>, "-- + std" in the
/// domain DDL), mapped as EF Core <b>shadow</b> properties.
/// <para>
/// Why shadow properties: who/when changed a row is a persistence concern, not domain state.
/// Aggregates stay free of four bookkeeping properties and of the actor/clock plumbing every
/// domain method would otherwise need; <see cref="AuditStampingInterceptor"/> fills them from
/// <c>ICurrentUser</c> and <c>TimeProvider</c> on every save, so no handler can forget them.
/// Read models (Dapper) read the columns directly; a handler that needs them after a write
/// reads them back with <see cref="Read"/>.
/// </para>
/// </summary>
public static class AuditColumns
{
    public const string CreatedAt = "CreatedAt";

    public const string CreatedBy = "CreatedBy";

    public const string UpdatedAt = "UpdatedAt";

    public const string UpdatedBy = "UpdatedBy";

    /// <summary>
    /// Maps the four standard columns as shadow properties of <typeparamref name="TEntity"/>.
    /// Column names come from the snake_case convention (<c>created_at</c>, ...).
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasStandardAuditColumns<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<DateTimeOffset>(CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property<Guid>(CreatedBy).IsRequired();
        builder.Property<DateTimeOffset>(UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property<Guid>(UpdatedBy).IsRequired();

        return builder;
    }

    /// <summary>Reads the audit values of a tracked entity (after a save they hold what was written).</summary>
    public static AuditInfo Read(EntityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AuditInfo(
            (DateTimeOffset)entry.Property(CreatedAt).CurrentValue!,
            (Guid)entry.Property(CreatedBy).CurrentValue!,
            (DateTimeOffset)entry.Property(UpdatedAt).CurrentValue!,
            (Guid)entry.Property(UpdatedBy).CurrentValue!);
    }
}

/// <summary>Audit values of one row.</summary>
public readonly record struct AuditInfo(DateTimeOffset CreatedAt, Guid CreatedBy, DateTimeOffset UpdatedAt, Guid UpdatedBy);
