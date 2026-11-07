using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Shouldly;
using static Nexus.BuildingBlocks.Messaging.Tests.Inbox.InboxTestHost;

namespace Nexus.BuildingBlocks.Messaging.Tests.Inbox;

/// <summary>
/// Consumer idempotency (conventions-events.md §7; conventions-testing.md §3 "Consumidor":
/// processing the same event_id twice produces a single effect), against a real Postgres
/// (bootstrap roles + Flyway), as the service role <c>svc_operations</c>.
/// </summary>
public sealed class InboxProcessorTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider _host = null!;

    public async ValueTask InitializeAsync()
    {
        await EnsureSchemaAsync(db, Ct);
        _host = Build(db);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact(DisplayName = "EV-INBOX: the same event_id processed twice by one consumer group produces a single effect")]
    public async Task SameEventTwice_SingleEffect()
    {
        var group = NewGroup();
        var envelope = Envelope(actorUserId: Guid.CreateVersion7());
        var invocations = 0;

        Task Effect(IServiceProvider sp, CancellationToken ct)
        {
            Interlocked.Increment(ref invocations);
            return WriteEffectRowAsync(sp, group, envelope.EventId, ct);
        }

        (await ConsumeAsync(_host, group, envelope, ActingIdentity.System, Effect, Ct)).ShouldBe(InboxResult.Processed);
        (await ConsumeAsync(_host, group, envelope, ActingIdentity.System, Effect, Ct)).ShouldBe(InboxResult.AlreadyProcessed);

        invocations.ShouldBe(1, "a duplicate must not even run the effect");
        (await CountEffectRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
    }

    [Fact(DisplayName = "EV-INBOX: the inbox row and the effect commit in the same transaction")]
    public async Task InboxRowAndEffect_SameTransaction()
    {
        var group = NewGroup();
        var envelope = Envelope();

        await ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            (sp, ct) => WriteEffectRowAsync(sp, group, envelope.EventId, ct), Ct);

        var inboxXmin = await db.ScalarAsync<string>(
            "SELECT xmin::text FROM inbox.processed_message WHERE consumer = $1 AND event_id = $2", group, envelope.EventId);
        var effectXmin = await db.ScalarAsync<string>(
            $"SELECT xmin::text FROM {Schema}.effect_row WHERE consumer = $1 AND event_id = $2", group, envelope.EventId);
        inboxXmin.ShouldNotBeNullOrEmpty();
        effectXmin.ShouldBe(inboxXmin);
    }

    [Fact(DisplayName = "EV-INBOX: the effect throws => no inbox row, the effect is rolled back, and a redelivery processes it")]
    public async Task EffectThrows_RollsBackInboxRowAndEffect()
    {
        var group = NewGroup();
        var envelope = Envelope();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => ConsumeAsync(
            _host, group, envelope, ActingIdentity.System,
            async (sp, ct) =>
            {
                await WriteEffectRowAsync(sp, group, envelope.EventId, ct);
                throw new InvalidOperationException("effect failed after writing");
            },
            Ct));
        ex.Message.ShouldBe("effect failed after writing");

        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(0);
        (await CountEffectRowsAsync(db, group, envelope.EventId)).ShouldBe(0);

        // Not marked processed: the retry runs the effect.
        (await ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            (sp, ct) => WriteEffectRowAsync(sp, group, envelope.EventId, ct), Ct)).ShouldBe(InboxResult.Processed);
        (await CountEffectRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
    }

    [Fact(DisplayName = "EV-INBOX: two concurrent deliveries of the same event_id => exactly one effect (the second waits on the PK)")]
    public async Task ConcurrentDuplicates_ExactlyOneEffect()
    {
        var group = NewGroup();
        var envelope = Envelope();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInvoked = 0;

        var first = Task.Run(() => ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            async (sp, ct) =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(ct);
                await WriteEffectRowAsync(sp, group, envelope.EventId, ct);
            },
            Ct), Ct);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        // The first transaction holds the uncommitted inbox row; start the duplicate now.
        var second = Task.Run(() => ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            (sp, ct) =>
            {
                Interlocked.Increment(ref secondInvoked);
                return WriteEffectRowAsync(sp, group, envelope.EventId, ct);
            },
            Ct), Ct);

        await WaitUntilAnInboxInsertIsBlockedAsync();
        second.IsCompleted.ShouldBeFalse("the duplicate must wait for the first transaction, not race past it");

        releaseFirst.SetResult();
        var results = await Task.WhenAll(first, second);

        results[0].ShouldBe(InboxResult.Processed);
        results[1].ShouldBe(InboxResult.AlreadyProcessed);
        secondInvoked.ShouldBe(0);
        (await CountEffectRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
    }

    [Fact(DisplayName = "EV-INBOX: if the first of two concurrent deliveries fails, the waiting duplicate runs the effect once")]
    public async Task ConcurrentDuplicates_FirstFails_SecondProcesses()
    {
        var group = NewGroup();
        var envelope = Envelope();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(() => ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            async (sp, ct) =>
            {
                await WriteEffectRowAsync(sp, group, envelope.EventId, ct);
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(ct);
                throw new InvalidOperationException("first delivery failed");
            },
            Ct), Ct);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var second = Task.Run(() => ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            (sp, ct) => WriteEffectRowAsync(sp, group, envelope.EventId, ct), Ct), Ct);

        await WaitUntilAnInboxInsertIsBlockedAsync();
        releaseFirst.SetResult();

        await Should.ThrowAsync<InvalidOperationException>(() => first);
        (await second).ShouldBe(InboxResult.Processed);
        (await CountEffectRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
    }

    [Fact(DisplayName = "EV-INBOX: different consumer groups each process the same event_id once")]
    public async Task DifferentGroups_EachProcessOnce()
    {
        var groupA = NewGroup();
        var groupB = NewGroup();
        var envelope = Envelope();

        foreach (var group in new[] { groupA, groupB, groupA, groupB })
        {
            await ConsumeAsync(_host, group, envelope, ActingIdentity.System,
                (sp, ct) => WriteEffectRowAsync(sp, group, envelope.EventId, ct), Ct);
        }

        (await CountEffectRowsAsync(db, groupA, envelope.EventId)).ShouldBe(1);
        (await CountEffectRowsAsync(db, groupB, envelope.EventId)).ShouldBe(1);
        (await CountInboxRowsAsync(db, groupA, envelope.EventId)).ShouldBe(1);
        (await CountInboxRowsAsync(db, groupB, envelope.EventId)).ShouldBe(1);
    }

    [Fact(DisplayName = "EV-INBOX: OnBehalfOfActor runs as the event's actor_user_id, with the event's correlation id")]
    public async Task OnBehalfOfActor_SetsActorAndCorrelation()
    {
        var group = NewGroup();
        var actor = Guid.CreateVersion7();
        var envelope = Envelope(actorUserId: actor);

        await ConsumeAsync(_host, group, envelope, ActingIdentity.OnBehalfOfActor,
            (sp, ct) => WriteEffectRowAsync(sp, group, envelope.EventId, ct), Ct);

        (await SeenAsync("seen_user_id", group, envelope.EventId)).ShouldBe(actor.ToString("D"));
        (await SeenAsync("seen_correlation_id", group, envelope.EventId)).ShouldBe(envelope.CorrelationId.ToString("D"));
    }

    [Fact(DisplayName = "EV-INBOX: System runs as the SYSTEM user even when the event has an actor")]
    public async Task System_SetsSystemUser()
    {
        var group = NewGroup();
        var envelope = Envelope(actorUserId: Guid.CreateVersion7());

        await ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            (sp, ct) => WriteEffectRowAsync(sp, group, envelope.EventId, ct), Ct);

        SystemUser.Id.ShouldBe(OrgFixture.SystemUserId, "must be the seeded SYSTEM user (db/seed/common)");
        (await SeenAsync("seen_user_id", group, envelope.EventId)).ShouldBe(SystemUser.Id.ToString("D"));
    }

    [Fact(DisplayName = "EV-INBOX: OnBehalfOfActor without actor_user_id falls back to the SYSTEM user")]
    public async Task OnBehalfOfActor_WithoutActor_FallsBackToSystem()
    {
        var group = NewGroup();
        var envelope = Envelope(actorUserId: null);

        await ConsumeAsync(_host, group, envelope, ActingIdentity.OnBehalfOfActor,
            (sp, ct) => WriteEffectRowAsync(sp, group, envelope.EventId, ct), Ct);

        (await SeenAsync("seen_user_id", group, envelope.EventId)).ShouldBe(SystemUser.Id.ToString("D"));
    }

    [Fact(DisplayName = "EV-INBOX: an event emitted by the effect goes to the outbox with actor = consumer identity and causation = consumed event_id")]
    public async Task EffectEvent_CarriesConsumerIdentityAndCausation()
    {
        var group = NewGroup();
        var actor = Guid.CreateVersion7();
        var envelope = Envelope(actorUserId: actor);
        var emittedId = Guid.CreateVersion7();

        await ConsumeAsync(_host, group, envelope, ActingIdentity.OnBehalfOfActor,
            (sp, _) =>
            {
                sp.GetRequiredService<IUnitOfWork>().AddEvent(new TestEvent(emittedId, "from-consumer"), aggregateVersion: 1);
                return Task.CompletedTask;
            },
            Ct);

        var headers = await db.ScalarAsync<string>("SELECT headers::text FROM outbox.message WHERE aggregate_id = $1", emittedId);
        using var json = JsonDocument.Parse(headers!);
        json.RootElement.GetProperty(OutboxHeaders.ActorUserId).GetGuid().ShouldBe(actor);
        json.RootElement.GetProperty(OutboxHeaders.CorrelationId).GetGuid().ShouldBe(envelope.CorrelationId);
        json.RootElement.GetProperty(OutboxHeaders.CausationId).GetGuid().ShouldBe(envelope.EventId);
    }

    [Fact(DisplayName = "EV-INBOX: processed_at comes from the injected TimeProvider")]
    public async Task ProcessedAt_FromTimeProvider()
    {
        var now = new DateTimeOffset(2026, 11, 7, 10, 30, 15, TimeSpan.Zero).AddTicks(1234560); // whole microseconds
        await using var host = Build(db, clock: new FixedTimeProvider(now));
        var group = NewGroup();
        var envelope = Envelope();

        await ConsumeAsync(host, group, envelope, ActingIdentity.System, (_, _) => Task.CompletedTask, Ct);

        var processedAt = await db.ScalarAsync<DateTime>(
            "SELECT processed_at FROM inbox.processed_message WHERE consumer = $1 AND event_id = $2", group, envelope.EventId);
        processedAt.ShouldBe(now.UtcDateTime);
    }

    [Fact(DisplayName = "EV-INBOX: an effect that commits the unit of work itself is Processed (inbox row committed with it)")]
    public async Task EffectCommitsItself_Processed()
    {
        var group = NewGroup();
        var envelope = Envelope();

        var result = await ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            async (sp, ct) =>
            {
                await WriteEffectRowAsync(sp, group, envelope.EventId, ct);
                await sp.GetRequiredService<IRequestTransaction>().CommitAsync(ct); // as a command handler does
            },
            Ct);

        result.ShouldBe(InboxResult.Processed);
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
        (await CountEffectRowsAsync(db, group, envelope.EventId)).ShouldBe(1);
    }

    [Fact(DisplayName = "EV-INBOX: an effect that rolls back the unit of work itself is never acknowledged as processed")]
    public async Task EffectRollsBackItself_Throws()
    {
        var group = NewGroup();
        var envelope = Envelope();

        await Should.ThrowAsync<InvalidOperationException>(() => ConsumeAsync(_host, group, envelope, ActingIdentity.System,
            async (sp, ct) =>
            {
                await WriteEffectRowAsync(sp, group, envelope.EventId, ct);
                await sp.GetRequiredService<IRequestTransaction>().RollbackAsync(ct);
            },
            Ct));

        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(0);
        (await CountEffectRowsAsync(db, group, envelope.EventId)).ShouldBe(0);
    }

    [Fact(DisplayName = "EV-INBOX: in a host with an HTTP ICurrentUser, only consumer scopes take the event's identity, and only while the processor runs")]
    public async Task HttpHost_ConsumerIdentityOnlyInConsumerScopes()
    {
        var httpUserId = Guid.CreateVersion7();
        await using var host = Build(db, httpContextUser: true);

        await InHttpRequestAsync(host, httpUserId, sp =>
        {
            sp.GetRequiredService<ICurrentUser>().UserId.ShouldBe(httpUserId);
            return Task.CompletedTask;
        });

        var group = NewGroup();
        var actor = Guid.CreateVersion7();
        var envelope = Envelope(actorUserId: actor);
        Guid? userInsideEffect = null;
        (await ConsumeAsync(host, group, envelope, ActingIdentity.OnBehalfOfActor,
            async (sp, ct) =>
            {
                userInsideEffect = sp.GetRequiredService<ICurrentUser>().UserId;
                await WriteEffectRowAsync(sp, group, envelope.EventId, ct);
            },
            Ct,
            afterProcessing: sp =>
            {
                // The consumer scope after the processor returned: no identity left behind.
                sp.GetRequiredService<ConsumerIdentity>().UserId.ShouldBeNull();
                sp.GetRequiredService<ICurrentUser>().IsAuthenticated.ShouldBeFalse();
                sp.GetRequiredService<ICurrentUser>().UserId.ShouldBeNull();
            })).ShouldBe(InboxResult.Processed);

        userInsideEffect.ShouldBe(actor);
        (await SeenAsync("seen_user_id", group, envelope.EventId)).ShouldBe(actor.ToString("D"));

        // A request served after the consumption still sees its own user.
        await InHttpRequestAsync(host, httpUserId, sp =>
        {
            sp.GetRequiredService<ICurrentUser>().UserId.ShouldBe(httpUserId);
            return Task.CompletedTask;
        });
    }

    [Fact(DisplayName = "EV-INBOX (ADR-0017): the inbox processor run inside an authenticated HTTP request is refused and the request's ICurrentUser is unchanged")]
    public async Task InsideAuthenticatedHttpRequest_IsRefused_UserUnchanged()
    {
        var httpUserId = Guid.CreateVersion7();
        await using var host = Build(db, httpContextUser: true);
        var group = NewGroup();
        var envelope = Envelope(actorUserId: Guid.CreateVersion7());
        var invoked = false;

        await InHttpRequestAsync(host, httpUserId, async sp =>
        {
            var processor = ActivatorUtilities.CreateInstance<InboxProcessor>(sp);
            await Should.ThrowAsync<InvalidOperationException>(() => processor.ProcessOnceAsync(
                group, envelope, ActingIdentity.OnBehalfOfActor, _ => { invoked = true; return Task.CompletedTask; }, Ct));

            sp.GetRequiredService<ICurrentUser>().UserId.ShouldBe(httpUserId);
            sp.GetRequiredService<ConsumerIdentity>().UserId.ShouldBeNull();
        });

        invoked.ShouldBeFalse();
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(0);
    }

    [Fact(DisplayName = "EV-INBOX (ADR-0017): a scope whose ICurrentUser is already authenticated (no HttpContext) is refused before the identity is switched")]
    public async Task AlreadyAuthenticatedScope_IsRefused()
    {
        var realUser = Guid.CreateVersion7();
        await using var host = Build(db, fixedUser: new TestCurrentUser(realUser));
        var group = NewGroup();
        var envelope = Envelope(actorUserId: Guid.CreateVersion7());
        var invoked = false;
        Guid? userAfter = null;

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => ConsumeAsync(host, group, envelope, ActingIdentity.OnBehalfOfActor,
            (_, _) => { invoked = true; return Task.CompletedTask; }, Ct,
            afterProcessing: sp => userAfter = sp.GetRequiredService<ICurrentUser>().UserId));

        ex.Message.ShouldContain("already has an authenticated ICurrentUser");
        invoked.ShouldBeFalse();
        userAfter.ShouldBe(realUser);
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(0);
    }

    [Fact(DisplayName = "EV-INBOX (ADR-0017): the inbox processor is refused inside any HTTP request, even an anonymous one")]
    public async Task InsideAnonymousHttpRequest_IsRefused()
    {
        await using var host = Build(db, httpContextUser: true);
        var group = NewGroup();
        var envelope = Envelope();
        var invoked = false;

        await InHttpRequestAsync(host, userId: null, async sp =>
        {
            var processor = ActivatorUtilities.CreateInstance<InboxProcessor>(sp);
            var ex = await Should.ThrowAsync<InvalidOperationException>(() => processor.ProcessOnceAsync(
                group, envelope, ActingIdentity.System, _ => { invoked = true; return Task.CompletedTask; }, Ct));
            ex.Message.ShouldContain("HTTP request");
            sp.GetRequiredService<ConsumerIdentity>().UserId.ShouldBeNull();
        });

        invoked.ShouldBeFalse();
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(0);
    }

    [Fact(DisplayName = "EV-INBOX: the consumer identity is cleared when the processor returns, whether the effect succeeded or threw")]
    public async Task ConsumerIdentity_ClearedAfterProcessing()
    {
        var group = NewGroup();
        var states = new List<(Guid? Identity, bool Authenticated)>();
        void Capture(IServiceProvider sp) => states.Add((
            sp.GetRequiredService<ConsumerIdentity>().UserId, sp.GetRequiredService<ICurrentUser>().IsAuthenticated));

        await ConsumeAsync(_host, group, Envelope(), ActingIdentity.System, (_, _) => Task.CompletedTask, Ct, Capture);
        await Should.ThrowAsync<InvalidOperationException>(() => ConsumeAsync(_host, group, Envelope(), ActingIdentity.System,
            (_, _) => throw new InvalidOperationException("effect failed"), Ct, Capture));

        states.ShouldBe([(null, false), (null, false)]);
    }

    [Fact(DisplayName = "EV-INBOX: an ICurrentUser registered AFTER AddNexusInbox (plain AddScoped) is detected and the processor refuses to begin")]
    public async Task CurrentUserRegisteredAfterInbox_IsRejected()
    {
        await using var host = Build(db, registeredAfterInbox: new TestCurrentUser(userId: null, isAuthenticated: false));
        var group = NewGroup();
        var envelope = Envelope();
        var invoked = false;

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => ConsumeAsync(host, group, envelope, ActingIdentity.System,
            (_, _) => { invoked = true; return Task.CompletedTask; }, Ct));

        ex.Message.ShouldContain("does not reflect the consumer identity");
        invoked.ShouldBeFalse();
        (await CountInboxRowsAsync(db, group, envelope.EventId)).ShouldBe(0);
    }

    [Fact(DisplayName = "EV-INBOX: the processor refuses a scope whose unit of work was already begun")]
    public async Task AlreadyActiveUnitOfWork_IsRejected()
    {
        await using var host = Build(db, fixedUser: new TestCurrentUser(Guid.CreateVersion7()));
        await using var scope = host.CreateAsyncScope();
        var correlation = scope.ServiceProvider.GetRequiredService<CorrelationContext>();
        correlation.CorrelationId = Guid.CreateVersion7();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);

        var invoked = false;
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            ActivatorUtilities.CreateInstance<InboxProcessor>(scope.ServiceProvider).ProcessOnceAsync(
                NewGroup(), Envelope(), ActingIdentity.System, _ => { invoked = true; return Task.CompletedTask; }, Ct));
        ex.Message.ShouldContain("already active");
        invoked.ShouldBeFalse();
    }

    private Task<string?> SeenAsync(string column, string group, Guid eventId) =>
        db.ScalarAsync<string>($"SELECT {column} FROM {Schema}.effect_row WHERE consumer = $1 AND event_id = $2", group, eventId);

    /// <summary>Polls pg_stat_activity until some backend waits on a lock while inserting into the inbox.</summary>
    private async Task WaitUntilAnInboxInsertIsBlockedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var blocked = await db.ScalarAsync<long>(
                "SELECT count(*) FROM pg_stat_activity " +
                "WHERE wait_event_type = 'Lock' AND query LIKE 'INSERT INTO inbox.processed_message%'");
            if (blocked > 0)
            {
                return;
            }

            await Task.Delay(20, Ct);
        }

        throw new TimeoutException("The duplicate delivery never blocked on the inbox primary key.");
    }
}
