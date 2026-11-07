using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Npgsql;

namespace Nexus.BuildingBlocks.Messaging.Tests.Inbox;

/// <summary>
/// The DI wiring of a service host that consumes events (AddNexusApplication +
/// AddNexusPersistence + AddNexusInbox), on the fixture's real Postgres as <c>svc_operations</c>.
/// </summary>
internal static class InboxTestHost
{
    public const string ServiceRole = "svc_operations";

    public const string Schema = "test_inbox_runtime";

    /// <summary>
    /// Test-only table the effects write to, created at run time in the throw-away container
    /// (never a migration), as the owner, with grants for the service role only.
    /// </summary>
    private const string Ddl = """
        CREATE SCHEMA IF NOT EXISTS test_inbox_runtime;
        CREATE TABLE IF NOT EXISTS test_inbox_runtime.effect_row (
          id                  uuid PRIMARY KEY,
          consumer            text NOT NULL,
          event_id            uuid NOT NULL,
          seen_user_id        text NULL,
          seen_correlation_id text NULL
        );
        REVOKE ALL ON SCHEMA test_inbox_runtime FROM PUBLIC;
        GRANT USAGE ON SCHEMA test_inbox_runtime TO svc_operations;
        GRANT SELECT, INSERT ON test_inbox_runtime.effect_row TO svc_operations;
        """;

    public static async Task EnsureSchemaAsync(PostgresFixture db, CancellationToken ct)
    {
        await using var connection = await db.OpenAsOwnerAsync(ct);
        await using var command = new NpgsqlCommand(Ddl, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <param name="fixedUser">A FIXED <see cref="ICurrentUser"/> registered before AddNexusInbox: authenticated in every scope.</param>
    /// <param name="clock">Replaces the default clock (registered before AddNexusApplication, which uses TryAdd).</param>
    /// <param name="httpContextUser">
    /// Simulates an HTTP host as AddNexusAuthentication wires it: <see cref="IHttpContextAccessor"/> plus an
    /// <see cref="ICurrentUser"/> that is authenticated only inside a request (see <see cref="InHttpRequestAsync"/>).
    /// </param>
    /// <param name="registeredAfterInbox">A plain <c>AddScoped&lt;ICurrentUser&gt;</c> AFTER AddNexusInbox (a miswired host).</param>
    public static ServiceProvider Build(
        PostgresFixture db,
        ICurrentUser? fixedUser = null,
        TimeProvider? clock = null,
        bool httpContextUser = false,
        ICurrentUser? registeredAfterInbox = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddNexusApplication();
        services.AddSingleton(db.GetServiceDataSource(ServiceRole));
        services.AddSingleton<IEventSchemaProvider>(new FileSystemEventSchemaProvider(RepoRoot.Find("contracts/events/_test")));
        services.AddNexusPersistence();
        if (fixedUser is not null)
        {
            services.AddScoped(_ => fixedUser);
        }

        if (httpContextUser)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        }

        services.AddNexusInbox();
        if (registeredAfterInbox is not null)
        {
            services.AddScoped(_ => registeredAfterInbox);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    public static EventEnvelope Envelope(Guid? eventId = null, Guid? actorUserId = null, Guid? correlationId = null) =>
        new(
            EventId: eventId ?? Guid.CreateVersion7(),
            EventType: "test.test-event.created",
            AggregateType: "test-event",
            AggregateId: Guid.CreateVersion7(),
            AggregateVersion: 1,
            OccurredAt: DateTimeOffset.UtcNow,
            ActorUserId: actorUserId,
            SchemaVersion: 1,
            CorrelationId: correlationId ?? Guid.CreateVersion7(),
            CausationId: Guid.CreateVersion7(),
            LegalEntityId: null);

    /// <summary>
    /// What a consumer does per message (as NexusConsumer does): one DI scope, the processor
    /// instantiated in it (never resolved from DI, ADR-0017) and the effect's services resolved from
    /// it. <paramref name="effect"/> receives the scope so it can reach the active unit of work (as a
    /// real effect would via constructor injection). <paramref name="afterProcessing"/> inspects the
    /// same scope once the processor returned or threw.
    /// </summary>
    public static async Task<InboxResult> ConsumeAsync(
        IServiceProvider root,
        string consumerGroup,
        EventEnvelope envelope,
        ActingIdentity identity,
        Func<IServiceProvider, CancellationToken, Task> effect,
        CancellationToken ct,
        Action<IServiceProvider>? afterProcessing = null)
    {
        await using var scope = root.CreateAsyncScope();
        var processor = ActivatorUtilities.CreateInstance<InboxProcessor>(scope.ServiceProvider);
        try
        {
            return await processor.ProcessOnceAsync(
                consumerGroup, envelope, identity, token => effect(scope.ServiceProvider, token), ct);
        }
        finally
        {
            afterProcessing?.Invoke(scope.ServiceProvider);
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> in a new DI scope inside a simulated HTTP request (an
    /// <see cref="HttpContext"/> set on the accessor; authenticated as <paramref name="userId"/> when given).
    /// Runs on its own execution context, so the request does not leak into the caller.
    /// </summary>
    public static Task InHttpRequestAsync(IServiceProvider root, Guid? userId, Func<IServiceProvider, Task> body) =>
        Task.Run(async () =>
        {
            var accessor = root.GetRequiredService<IHttpContextAccessor>();
            var context = new DefaultHttpContext();
            if (userId is { } id)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(HttpContextCurrentUser.UserIdClaim, id.ToString("D"))], "test"));
            }

            accessor.HttpContext = context;
            try
            {
                await using var scope = root.CreateAsyncScope();
                await body(scope.ServiceProvider);
            }
            finally
            {
                accessor.HttpContext = null;
            }
        });

    /// <summary>A typical effect: a business write on the active unit of work, recording the RLS session it saw.</summary>
    public static async Task WriteEffectRowAsync(IServiceProvider scope, string consumerGroup, Guid eventId, CancellationToken ct)
    {
        var uow = scope.GetRequiredService<IUnitOfWork>();
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {Schema}.effect_row (id, consumer, event_id, seen_user_id, seen_correlation_id) " +
            "VALUES ($1, $2, $3, current_setting('app.current_user_id', true), current_setting('app.correlation_id', true))",
            uow.Connection,
            uow.Transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.CreateVersion7() });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = consumerGroup });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<long> CountEffectRowsAsync(PostgresFixture db, string consumerGroup, Guid eventId) =>
        (await db.ScalarAsync<long>(
            $"SELECT count(*) FROM {Schema}.effect_row WHERE consumer = $1 AND event_id = $2", consumerGroup, eventId));

    public static async Task<long> CountInboxRowsAsync(PostgresFixture db, string consumerGroup, Guid eventId) =>
        (await db.ScalarAsync<long>(
            "SELECT count(*) FROM inbox.processed_message WHERE consumer = $1 AND event_id = $2", consumerGroup, eventId));

    public static string NewGroup() => $"operations.test-{Guid.CreateVersion7():N}";
}

/// <summary>
/// Stand-in for HttpCurrentUser (BuildingBlocks.Web): authenticated only while the scope runs inside an
/// HTTP request whose principal is authenticated; reads the user id from a claim.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string UserIdClaim = "test-user-id";

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string? Oid => null;

    public string? Subject => null;

    public Guid? UserId => IsAuthenticated && accessor.HttpContext!.User.FindFirst(UserIdClaim) is { } claim ? Guid.Parse(claim.Value) : null;

    public Guid? PartyId => null;

    public Guid? BusinessUnitId => null;
}

/// <summary>A clock frozen at a whole-microsecond instant.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
