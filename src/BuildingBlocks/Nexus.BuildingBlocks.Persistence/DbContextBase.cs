using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// Base class for every service's EF Core context (conventions-backend.md §4):
/// <list type="bullet">
///   <item>Runs on the <see cref="IUnitOfWork"/>'s connection and transaction: it enlists
///   itself on construction and configures Npgsql with <see cref="IUnitOfWork.Connection"/>, so
///   it must first be used after <see cref="IUnitOfWork.BeginAsync"/>.</item>
///   <item>snake_case names (EFCore.NamingConventions).</item>
///   <item>Optimistic concurrency on the <c>xmin</c> system column for every entity type
///   (BE-R3), applied by a model-finalizing convention so that services cannot forget it.</item>
///   <item>Aggregate roots also persist <c>AggregateVersion</c> in <c>aggregate_version</c>,
///   a second concurrency token (ADR-0019), so event versions survive between commands and two
///   concurrent commands cannot emit the same version.</item>
///   <item>BE-R2: <c>SaveChanges</c>/<c>SaveChangesAsync</c> throw when called directly; only
///   <see cref="IUnitOfWork.CommitAsync"/> saves.</item>
///   <item>No EF migrations (ADR-0001): schemas come from Flyway SQL.</item>
/// </list>
/// </summary>
public abstract class DbContextBase : DbContext
{
    private readonly IUnitOfWork _unitOfWork;
    private bool _savingFromUnitOfWork;

    protected DbContextBase(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _unitOfWork.Enlist(this);
    }

    /// <summary>
    /// For contexts that need extra options (logging, interceptors) — override
    /// <see cref="ConfigureOptions"/> for those, not this constructor's <paramref name="options"/>:
    /// the Npgsql connection is always the unit of work's, whatever <paramref name="options"/> says.
    /// </summary>
    protected DbContextBase(DbContextOptions options, IUnitOfWork unitOfWork)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _unitOfWork.Enlist(this);
    }

    public sealed override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw DirectSaveForbidden();

    public sealed override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        _savingFromUnitOfWork
            ? base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
            : throw DirectSaveForbidden();

    internal async Task SaveChangesFromUnitOfWorkAsync(CancellationToken cancellationToken)
    {
        _savingFromUnitOfWork = true;
        try
        {
            await SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _savingFromUnitOfWork = false;
        }
    }

    /// <summary>
    /// Sealed on purpose (BE-R1): a derived context must not be able to override this and quietly
    /// point itself at some other connection, which would run its queries outside the unit of
    /// work's transaction and RLS session without any visible error. Override
    /// <see cref="ConfigureOptions"/> instead for anything additive (logging, interceptors).
    /// </summary>
    protected sealed override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ConfigureOptions(optionsBuilder);
        if (_unitOfWork.IsActive)
        {
            optionsBuilder.UseNpgsql(_unitOfWork.Connection);
        }
        else
        {
            // No transaction yet (e.g. the context is only being used to inspect the EF model,
            // before BeginAsync): still needs a provider to build the model, but there is no real
            // connection to force and no RLS session to leak from.
            optionsBuilder.UseNpgsql();
        }

        optionsBuilder.UseSnakeCaseNamingConvention();
        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>
    /// Override to add options that do not touch the database provider or connection (logging,
    /// interceptors, query tracking behavior, ...). Called before the unit of work's connection is
    /// applied; calling <c>UseNpgsql</c> here is pointless and EF will reject the conflicting
    /// configuration that follows.
    /// </summary>
    protected virtual void ConfigureOptions(DbContextOptionsBuilder optionsBuilder)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new AggregateRootMembersConvention());
        configurationBuilder.Conventions.Add(_ => new AggregateVersionConvention());
        configurationBuilder.Conventions.Add(_ => new XminConcurrencyTokenConvention());
        base.ConfigureConventions(configurationBuilder);
    }

    private static InvalidOperationException DirectSaveForbidden() =>
        new("BE-R2: DbContext.SaveChanges is forbidden; changes are saved by IUnitOfWork.CommitAsync.");
}

/// <summary>
/// Keeps <c>DomainEvents</c> (in-memory bookkeeping of <see cref="Domain.AggregateRoot{TId}"/>)
/// out of the EF model, so that no service has to <c>Ignore</c> it by hand.
/// <c>AggregateVersion</c> is <b>not</b> ignored: it is persisted (ADR-0019) and configured by
/// <see cref="AggregateVersionConvention"/> once the model is complete.
/// </summary>
internal sealed class AggregateRootMembersConvention : IEntityTypeAddedConvention
{
    public void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context)
    {
        if (AggregateVersionConvention.FindAggregateRootType(entityTypeBuilder.Metadata.ClrType) is not null)
        {
            entityTypeBuilder.Ignore(nameof(Domain.AggregateRoot<int>.DomainEvents));
        }
    }
}

/// <summary>
/// ADR-0019: maps <see cref="Domain.AggregateRoot{TId}.AggregateVersion"/> to the
/// <c>aggregate_version</c> column of every aggregate root table (conventions-database.md §4,
/// "Tablas raíz de agregado") as an EF Core concurrency token:
/// <list type="bullet">
///   <item>Loaded with the aggregate, so <c>RaiseEvent</c> continues from the persisted count and
///   the <c>UnitOfWork</c> numbers each outbox row <c>AggregateVersion - n + i + 1</c>.</item>
///   <item>Concurrency token: <c>UPDATE … WHERE aggregate_version = &lt;loaded&gt;</c> (EF Core
///   compares the original value), so two concurrent commands on the same aggregate cannot both
///   commit the same version; the loser gets <see cref="DbUpdateConcurrencyException"/> → 409.
///   It sits next to <c>xmin</c> (<see cref="XminConcurrencyTokenConvention"/>), which keeps
///   backing the ETag/If-Match.</item>
///   <item>Never store-generated: the application always sends the value, on INSERT too (the
///   column's <c>DEFAULT 0</c> is only for SQL seeds that emit no event).</item>
///   <item>Materialized through the auto-property's backing field (the setter is private to
///   the domain).</item>
/// </list>
/// Runs at model finalization, after <c>OnModelCreating</c>, so services cannot forget it.
/// Skipped: owned and keyless types and types not mapped to a table (not aggregate roots with a
/// row of their own). A derived type of a TPH hierarchy inherits the property from its root.
/// </summary>
internal sealed class AggregateVersionConvention : IModelFinalizingConvention
{
    internal const string PropertyName = nameof(Domain.AggregateRoot<int>.AggregateVersion);

    internal const string ColumnName = "aggregate_version";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.BaseType is not null
                || entityType.IsOwned()
                || entityType.FindPrimaryKey() is null
                || entityType.GetTableName() is null
                || FindAggregateRootType(entityType.ClrType) is not { } aggregateRootType)
            {
                continue;
            }

            var member = aggregateRootType.GetProperty(PropertyName)!;
            var property = entityType.Builder.Property(member);
            if (property is null)
            {
                // Explicitly ignored by the service: only possible on purpose, so fail loudly
                // instead of silently emitting every event with version 1 again.
                throw new InvalidOperationException(
                    $"ADR-0019: {entityType.DisplayName()} is an aggregate root but {PropertyName} could not be mapped to '{ColumnName}'.");
            }

            property.HasColumnName(ColumnName);
            property.HasColumnType("bigint");
            property.IsRequired(true);
            property.IsConcurrencyToken(true);
            property.ValueGenerated(ValueGenerated.Never);

            // The setter is private to the domain: EF Core reads and writes the auto-property's
            // backing field (found by convention). Fail at model build, not at the first load.
            if (property.Metadata.FieldInfo is null && property.Metadata.PropertyInfo?.SetMethod is null)
            {
                throw new InvalidOperationException(
                    $"ADR-0019: EF Core cannot materialize {entityType.DisplayName()}.{PropertyName} (no backing field or setter found).");
            }
        }
    }

    /// <summary>The closed <c>AggregateRoot&lt;TId&gt;</c> in <paramref name="type"/>'s hierarchy, or null.</summary>
    internal static Type? FindAggregateRootType(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Domain.AggregateRoot<>))
            {
                return current;
            }
        }

        return null;
    }
}

/// <summary>
/// BE-R3: maps PostgreSQL's <c>xmin</c> system column as a shadow <see cref="uint"/> concurrency
/// token (column type <c>xid</c>, regenerated on every insert/update) on every root entity type
/// that is mapped to a table. It backs the API's ETag/If-Match (<see cref="RowVersion"/>).
///
/// This is what Npgsql's <c>UseXminAsConcurrencyToken()</c> used to do per entity (it is
/// obsolete since EFCore.PG 7 in favour of a <c>uint</c> row-version property); doing it in a
/// model-finalizing convention runs after <c>OnModelCreating</c>, so entities configured there
/// are covered too. Skipped: derived types (the root's token covers the row), owned types
/// (they share the owner's row or are versioned through it), keyless types and entities not
/// mapped to a table (views, queries), and entities that already map an <c>xmin</c> property.
/// Other concurrency tokens do not replace it: aggregate roots also carry
/// <c>aggregate_version</c> (<see cref="AggregateVersionConvention"/>, ADR-0019), and the ETag
/// still comes from <c>xmin</c>.
/// </summary>
internal sealed class XminConcurrencyTokenConvention : IModelFinalizingConvention
{
    internal const string PropertyName = "xmin";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.BaseType is not null
                || entityType.IsOwned()
                || entityType.FindPrimaryKey() is null
                || entityType.GetTableName() is null
                || entityType.FindProperty(PropertyName) is not null)
            {
                continue;
            }

            var property = entityType.Builder.Property(typeof(uint), PropertyName);
            if (property is null)
            {
                continue;
            }

            property.HasColumnName(PropertyName);
            property.HasColumnType("xid");
            property.IsConcurrencyToken(true);
            property.ValueGenerated(ValueGenerated.OnAddOrUpdate);
        }
    }
}
