using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.BuildingBlocks.Analytics.Audit;
using Nexus.BuildingBlocks.Analytics.Scope;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Npgsql;

namespace Nexus.BuildingBlocks.Analytics.Tests;

/// <summary>
/// <see cref="OrgFixture"/> (real migrations + common seed + the conventions-testing §4 users:
/// <c>Admin</c> SYSTEM_ADMIN/GLOBAL, <c>UserA</c> ORG_ADMIN/LE-A (CH), <c>UserB</c>
/// ORG_ADMIN/LE-B (DE), <c>WhA</c> WAREHOUSE_OPERATOR/WH-A) plus a <see cref="ClickHouseFixture"/>,
/// plus two extra users for scope edge cases: <see cref="BuViewer"/> (VIEWER on BU-A) and
/// <see cref="GroupViewer"/> (VIEWER on the test group). Every role above holds
/// <c>AUDIT:READ</c> by default (<c>db/seed/common/V202611082101__seed__permissions_0_8.sql</c>).
/// One per test class.
/// </summary>
public sealed class AnalyticsFixture : IAsyncLifetime
{
    public OrgFixture Org { get; } = new();

    public ClickHouseFixture ClickHouse { get; } = new();

    public Guid BuViewer { get; } = Guid.CreateVersion7();

    public Guid GroupViewer { get; } = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Org.InitializeAsync().AsTask(), ClickHouse.InitializeAsync().AsTask());
        await SeedUserAsync(BuViewer, "buviewer", "VIEWER", "BUSINESS_UNIT", Org.BusinessUnitA.Id);
        await SeedUserAsync(GroupViewer, "groupviewer", "VIEWER", "GROUP", Org.GroupId);
    }

    public async ValueTask DisposeAsync()
    {
        await Org.DisposeAsync();
        await ClickHouse.DisposeAsync();
    }

    /// <summary>
    /// A begun <see cref="UnitOfWork"/> as <paramref name="userId"/> on <c>svc_platform</c>, the way a
    /// request has it. Read-only use: its outbox writer throws, and disposing rolls back.
    /// </summary>
    public async Task<UnitOfWork> BeginAsync(Guid userId)
    {
        var unitOfWork = new UnitOfWork(
            Org.Database.GetServiceDataSource("svc_platform"),
            new TestCurrentUser(userId),
            new CorrelationContext { CorrelationId = Guid.CreateVersion7() },
            new NoOutboxWriter());
        await unitOfWork.BeginAsync(TestContext.Current.CancellationToken);
        return unitOfWork;
    }

    /// <summary>The audit reader exactly as DI builds it, on <paramref name="unitOfWork"/>.</summary>
    internal AuditTrailReader Reader(IUnitOfWork unitOfWork, ILogger<AuditTrailReader>? logger = null) =>
        new(new ScopeFilterProvider(unitOfWork), ClickHouse.CreateExecutor(), logger ?? NullLogger<AuditTrailReader>.Instance);

    /// <summary>Lists the audit page as <paramref name="userId"/>, in its own request transaction.</summary>
    internal async Task<Nexus.BuildingBlocks.Application.Paging.PagedResult<AuditEventRecord>> ListAsAsync(
        Guid userId, string aggregateType, Guid aggregateId, int page = 1, int pageSize = 50, ILogger<AuditTrailReader>? logger = null)
    {
        await using var unitOfWork = await BeginAsync(userId);
        return await Reader(unitOfWork, logger).ListAsync(aggregateType, aggregateId, page, pageSize, TestContext.Current.CancellationToken);
    }

    private async Task SeedUserAsync(Guid userId, string name, string roleCode, string scopeType, Guid scopeEntityId)
    {
        const string Std = "now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'";
        var partyId = Guid.CreateVersion7();
        await using var connection = await Org.Database.Admin.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        async Task Exec(string sql, params object[] values)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            foreach (var value in values)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value });
            }

            await command.ExecuteNonQueryAsync();
        }

        await Exec(
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, 'PERSON', $2, true, true, " + Std + ")",
            partyId,
            name);
        await Exec(
            "INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, NULL, $3, $4, 'de', true, " + Std + ")",
            userId,
            partyId,
            Guid.NewGuid().ToString("D"),
            name + "@test.nexus.local");
        await Exec(
            "INSERT INTO access.role_assignment (id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, (SELECT id FROM access.role WHERE code = $3), $4, $5, now(), NULL, " +
            "'00000000-0000-7000-8000-000000000001', 'SEED', NULL, NULL, " + Std + ")",
            Guid.CreateVersion7(),
            userId,
            roleCode,
            scopeType,
            scopeEntityId);
        await transaction.CommitAsync();
    }

    /// <summary>These tests only read; a write reaching the outbox is a test bug.</summary>
    private sealed class NoOutboxWriter : IOutboxWriter
    {
        public Task WriteAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<PendingDomainEvent> events,
            OutboxEnvelopeContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Analytics tests never commit.");
    }
}
