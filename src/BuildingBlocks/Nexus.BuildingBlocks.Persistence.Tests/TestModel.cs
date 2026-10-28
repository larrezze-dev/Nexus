using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Npgsql;

namespace Nexus.BuildingBlocks.Persistence.Tests;

/// <summary>Test aggregate persisted in uow_test.widget.</summary>
public sealed class Widget : AggregateRoot<Guid>
{
    private Widget(Guid id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public static Widget Create(Guid id, string name) => new(id, name);

    public void Rename(string newName, decimal price, DateTimeOffset at, Guid? legalEntityId = null)
    {
        Name = newName;
        RaiseEvent(new WidgetRenamed(Id, newName, at, price, new Dictionary<string, string> { ["de"] = newName }, null)
        {
            LegalEntityId = legalEntityId,
        });
    }

    public void MarkDeleted() => RaiseEvent(new WidgetDeleted(Id));

    /// <summary>Raises an event whose payload violates the schema (null in a non-nullable string).</summary>
    public void RenameWithInvalidPayload() =>
        RaiseEvent(new WidgetRenamed(Id, null!, DateTimeOffset.UtcNow, 1m, new Dictionary<string, string>(), null));
}

public sealed record WidgetRenamed(
    Guid WidgetId,
    string NewName,
    DateTimeOffset RenamedAt,
    decimal Price,
    IReadOnlyDictionary<string, string> Labels,
    string? Note) : IDomainEvent
{
    public string AggregateType => "widget";

    public Guid AggregateId => WidgetId;

    public string EventType => "test.widget.renamed";

    public int SchemaVersion => 1;

    public string Topic => "nexus.test.widget.renamed";

    public Guid? LegalEntityId { get; init; }
}

public sealed record WidgetDeleted(Guid WidgetId) : IDomainEvent
{
    public string AggregateType => "widget";

    public Guid AggregateId => WidgetId;

    public string EventType => "test.widget.deleted";

    public int SchemaVersion => 1;

    public string Topic => "nexus.test.widget.deleted";

    public Guid? LegalEntityId => null;
}

public sealed class WidgetDbContext : DbContextBase
{
    public WidgetDbContext(IUnitOfWork unitOfWork)
        : base(unitOfWork)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(b =>
        {
            b.ToTable("widget", "uow_test");
            b.HasKey(w => w.Id);
        });
    }
}

public sealed class TestCurrentUser(Guid? userId, bool isAuthenticated = true) : ICurrentUser
{
    public Guid? UserId { get; } = userId;

    public bool IsAuthenticated { get; } = isAuthenticated;

    public string? Oid => null;

    public string? Subject => null;

    public Guid? PartyId => null;

    public Guid? BusinessUnitId => null;
}

public sealed class TestIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7(TimeProvider.System.GetUtcNow());
}

/// <summary>Writes the outbox rows through the real writer, then fails before COMMIT.</summary>
public sealed class ThrowAfterWriteOutboxWriter(IOutboxWriter inner) : IOutboxWriter
{
    public async Task WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<PendingDomainEvent> events,
        OutboxEnvelopeContext context,
        CancellationToken cancellationToken)
    {
        await inner.WriteAsync(connection, transaction, events, context, cancellationToken);

        // Prove the rows really are in the transaction before we blow up.
        await using var probe = new NpgsqlCommand("SELECT 1", connection, transaction);
        await probe.ExecuteScalarAsync(cancellationToken);
        throw new SimulatedFailureException();
    }
}

public sealed class SimulatedFailureException : Exception
{
    public SimulatedFailureException()
        : base("Simulated failure after the outbox write and before COMMIT.")
    {
    }

    public SimulatedFailureException(string message)
        : base(message)
    {
    }

    public SimulatedFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
