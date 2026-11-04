using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Npgsql;

namespace Nexus.BuildingBlocks.Web.Tests.Authorization;

/// <summary>
/// <see cref="OrgFixture"/> (every migration + common seed + LE/BU/WH/users/assignments) plus:
/// <list type="bullet">
///   <item>one user whose only assignment is <c>ORG_ADMIN</c> at <c>BUSINESS_UNIT</c> BU-A,
///   to prove coverage below the legal-entity level (ACC-R4);</item>
///   <item>one user whose only assignment is <c>ORG_ADMIN</c> at <c>GROUP</c>
///   <see cref="OrgFixture.GroupId"/>, and a second, empty group <see cref="OtherGroupId"/>,
///   to prove GROUP coverage (ADR-0015).</item>
/// </list>
/// </summary>
public sealed class AccessPermissionDatabase : IAsyncLifetime
{
    public OrgFixture Org { get; } = new();

    public Guid BuAdminUserId { get; } = Guid.CreateVersion7();

    public Guid GroupAdminUserId { get; } = Guid.CreateVersion7();

    /// <summary>A second, real group with no legal entities and no assignments.</summary>
    public Guid OtherGroupId { get; } = Guid.CreateVersion7();

    public ServiceDataSource Platform => Org.Database.GetServiceDataSource("svc_platform");

    public async ValueTask InitializeAsync()
    {
        await Org.InitializeAsync();

        const string Std = "now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'";
        var partyId = Guid.CreateVersion7();
        await using var batch = Org.Database.Admin.CreateBatch();
        batch.BatchCommands.Add(Command(
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, 'PERSON', 'BU Admin Test', true, true, " + Std + ")",
            partyId));
        batch.BatchCommands.Add(Command(
            "INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, $3, '00000000-0000-7000-8000-00000000f0b1', 'buadmin@test.nexus.local', 'de', true, " + Std + ")",
            BuAdminUserId,
            partyId,
            Org.BusinessUnitA.Id));
        batch.BatchCommands.Add(Command(
            "INSERT INTO access.role_assignment (id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, (SELECT id FROM access.role WHERE code = 'ORG_ADMIN'), 'BUSINESS_UNIT', $3, now(), NULL, " +
            "'00000000-0000-7000-8000-000000000001', 'SEED', NULL, NULL, " + Std + ")",
            Guid.CreateVersion7(),
            BuAdminUserId,
            Org.BusinessUnitA.Id));

        var groupAdminPartyId = Guid.CreateVersion7();
        batch.BatchCommands.Add(Command(
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, 'PERSON', 'Group Admin Test', true, true, " + Std + ")",
            groupAdminPartyId));
        batch.BatchCommands.Add(Command(
            "INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, $3, '00000000-0000-7000-8000-00000000f0b2', 'groupadmin@test.nexus.local', 'de', true, " + Std + ")",
            GroupAdminUserId,
            groupAdminPartyId,
            Org.BusinessUnitA.Id));
        batch.BatchCommands.Add(Command(
            "INSERT INTO access.role_assignment (id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, (SELECT id FROM access.role WHERE code = 'ORG_ADMIN'), 'GROUP', $3, now(), NULL, " +
            "'00000000-0000-7000-8000-000000000001', 'SEED', NULL, NULL, " + Std + ")",
            Guid.CreateVersion7(),
            GroupAdminUserId,
            Org.GroupId));

        var otherGroupPartyId = Guid.CreateVersion7();
        batch.BatchCommands.Add(Command(
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, 'ORGANIZATION', 'Other Test Group', true, true, " + Std + ")",
            otherGroupPartyId));
        batch.BatchCommands.Add(Command(
            "INSERT INTO org.\"group\" (id, party_id, code, reporting_currency_code, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, 'OTHER-GROUP', 'CHF', " + Std + ")",
            OtherGroupId,
            otherGroupPartyId));
        await batch.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync() => Org.DisposeAsync();

    /// <summary>A real <see cref="UnitOfWork"/> as <c>svc_platform</c> for <paramref name="userId"/>.
    /// Never committed in these tests, so the outbox writer is never reached.</summary>
    public UnitOfWork CreateUnitOfWork(Guid userId) =>
        new(
            Platform,
            new TestCurrentUser(userId),
            new CorrelationContext { CorrelationId = Guid.CreateVersion7() },
            new UnusedOutboxWriter());

    private static NpgsqlBatchCommand Command(string sql, params object[] values)
    {
        var command = new NpgsqlBatchCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return command;
    }

    private sealed class UnusedOutboxWriter : IOutboxWriter
    {
        public Task WriteAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<PendingDomainEvent> events,
            OutboxEnvelopeContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Permission checks never commit.");
    }
}
