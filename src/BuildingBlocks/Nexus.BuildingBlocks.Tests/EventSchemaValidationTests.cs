using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Nexus.BuildingBlocks.Tests.Support;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// EV-R2: an event whose payload does not satisfy its <c>.avsc</c> aborts the transaction.
/// Every invalid event below claims the real contract of <see cref="TestEvent"/>
/// (<c>contracts/events/_test/test/test-event.created.v1.avsc</c>: <c>testEventId</c> uuid and
/// <c>name</c> string, both required), and each one breaks it in exactly one way, asserted via
/// the exception message so that no case passes for another case's reason. Every unit of work
/// also carries a valid business row and a valid event, to prove the whole transaction aborts,
/// not just the faulty event.
/// </summary>
public sealed class EventSchemaValidationTests(PostgresFixture db) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await RuntimeTestSchema.EnsureCreatedAsync(db, Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact(DisplayName = "EV-R2: payload with null in a required field aborts the transaction")]
    public Task NullInRequiredField_AbortsTransaction() =>
        AssertAbortsTransactionAsync(
            id => new TestEvent(id, Name: null!),
            "null is not allowed");

    [Fact(DisplayName = "EV-R2: payload with a wrong type (string where the schema says uuid) aborts the transaction")]
    public Task WrongFieldType_AbortsTransaction() =>
        AssertAbortsTransactionAsync(
            id => new TestEventWithStringId(id.ToString("D"), "wrong-type"),
            "value of CLR type String does not match");

    [Fact(DisplayName = "EV-R2: payload missing a field required by the schema aborts the transaction")]
    public Task MissingRequiredField_AbortsTransaction() =>
        AssertAbortsTransactionAsync(
            id => new TestEventWithoutName(id),
            "schema field has no matching public property");

    [Fact(DisplayName = "EV-R2: payload with a property the schema does not declare aborts the transaction")]
    public Task UndeclaredProperty_AbortsTransaction() =>
        AssertAbortsTransactionAsync(
            id => new TestEventWithExtraField(id, "extra", Extra: 42),
            "are not in schema");

    private async Task AssertAbortsTransactionAsync(Func<Guid, IDomainEvent> invalidEvent, string expectedReason)
    {
        var validId = Guid.CreateVersion7();
        var invalidId = Guid.CreateVersion7();

        await using (var uow = UnitOfWorks.Create(db.GetServiceDataSource(RuntimeTestSchema.OwningServiceRole), Guid.CreateVersion7()))
        {
            await uow.BeginAsync(Ct);
            await UnitOfWorks.InsertBusinessRowAsync(uow, validId, "valid", Ct);
            uow.AddEvent(new TestEvent(validId, "valid"), aggregateVersion: 1);
            uow.AddEvent(invalidEvent(invalidId), aggregateVersion: 1);

            var ex = await Should.ThrowAsync<EventPayloadSchemaException>(() => uow.CommitAsync(Ct));
            ex.Message.ShouldContain(expectedReason);
            uow.IsActive.ShouldBeFalse();
        }

        (await UnitOfWorks.CountBusinessRowsAsync(db, validId)).ShouldBe(0);
        (await UnitOfWorks.CountOutboxRowsAsync(db, validId)).ShouldBe(0);
        (await UnitOfWorks.CountOutboxRowsAsync(db, invalidId)).ShouldBe(0);
    }

    // The faulty events below expose no public property besides the IDomainEvent envelope
    // members and the payload under test, so each one breaks the contract in one way only.

    private sealed record TestEventWithStringId(string TestEventId, string Name) : IDomainEvent
    {
        public string AggregateType => "test-event";

        public Guid AggregateId => Guid.Parse(TestEventId);

        public string EventType => "test.test-event.created";

        public int SchemaVersion => 1;

        public string Topic => "nexus.test.test-event.created";

        public Guid? LegalEntityId => null;
    }

    private sealed record TestEventWithoutName(Guid TestEventId) : IDomainEvent
    {
        public string AggregateType => "test-event";

        public Guid AggregateId => TestEventId;

        public string EventType => "test.test-event.created";

        public int SchemaVersion => 1;

        public string Topic => "nexus.test.test-event.created";

        public Guid? LegalEntityId => null;
    }

    private sealed record TestEventWithExtraField(Guid TestEventId, string Name, int Extra) : IDomainEvent
    {
        public string AggregateType => "test-event";

        public Guid AggregateId => TestEventId;

        public string EventType => "test.test-event.created";

        public int SchemaVersion => 1;

        public string Topic => "nexus.test.test-event.created";

        public Guid? LegalEntityId => null;
    }
}
