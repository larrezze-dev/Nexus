using Nexus.BuildingBlocks.Messaging.Replica;
using Nexus.BuildingBlocks.Testing;
using Npgsql;
using NpgsqlTypes;
using Shouldly;

namespace Nexus.BuildingBlocks.Messaging.Tests.Replica;

/// <summary>
/// <see cref="VersionedReplica"/> against a real PostgreSQL 17 (<see cref="PostgresFixture"/>).
/// Each test creates its own scratch <c>*_ref</c> table (as <c>nexus_owner</c>) and writes it as
/// <c>svc_catalog</c>, a real non-owner service role, inside an explicit transaction -- the same
/// shape a consumer's <c>UnitOfWork</c> gives it.
/// </summary>
public sealed class VersionedReplicaTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private const string Schema = "replica_scratch";
    private const string ServiceRole = "svc_catalog";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "EV-ORDER: versions 3, 2, 4 -> ends at 4 and 2 is ignored (IgnoredStale)")]
    public async Task OutOfOrder_OlderVersionIsDiscarded()
    {
        var replica = await CreateReplicaAsync();
        var id = Guid.CreateVersion7();

        (await ApplyAsync(replica, id, 3, "v3")).ShouldBe(ReplicaApplyResult.Applied);
        (await ApplyAsync(replica, id, 2, "v2")).ShouldBe(ReplicaApplyResult.IgnoredStale);
        (await ReadAsync(replica, id)).ShouldBe((3L, "v3"));

        (await ApplyAsync(replica, id, 4, "v4")).ShouldBe(ReplicaApplyResult.Applied);
        (await ReadAsync(replica, id)).ShouldBe((4L, "v4"));
    }

    [Fact(DisplayName = "EV-ORDER: re-delivery of the applied version is ignored and does not overwrite")]
    public async Task EqualVersion_IsIgnored()
    {
        var replica = await CreateReplicaAsync();
        var id = Guid.CreateVersion7();

        (await ApplyAsync(replica, id, 5, "original")).ShouldBe(ReplicaApplyResult.Applied);
        (await ApplyAsync(replica, id, 5, "tampered")).ShouldBe(ReplicaApplyResult.IgnoredStale);

        (await ReadAsync(replica, id)).ShouldBe((5L, "original"));
    }

    [Fact(DisplayName = "EV-ORDER: concurrent consumers applying shuffled versions converge on the highest")]
    public async Task Concurrent_ShuffledVersions_ConvergeOnHighest()
    {
        var replica = await CreateReplicaAsync();
        var id = Guid.CreateVersion7();
        const int Versions = 40;

        // The row does not exist yet: several transactions race on the first INSERT as well.
        var shuffled = Enumerable.Range(1, Versions).OrderBy(_ => Random.Shared.Next()).ToArray();
        using var start = new SemaphoreSlim(0);
        var tasks = shuffled
            .Select(async version =>
            {
                await start.WaitAsync(Ct);
                return await ApplyAsync(replica, id, version, $"v{version}");
            })
            .ToArray();
        start.Release(Versions);

        var results = await Task.WhenAll(tasks);

        (await ReadAsync(replica, id)).ShouldBe(((long)Versions, $"v{Versions}"));
        results.ShouldContain(ReplicaApplyResult.Applied);
        (await db.ScalarAsync<long>($"SELECT count(*) FROM {Schema}.{replica.Table.Table}")).ShouldBe(1);
    }

    [Fact(DisplayName = "EV-ORDER: rows of different aggregates are versioned independently")]
    public async Task DifferentAggregates_AreIndependent()
    {
        var replica = await CreateReplicaAsync();
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        (await ApplyAsync(replica, a, 7, "a7")).ShouldBe(ReplicaApplyResult.Applied);
        (await ApplyAsync(replica, b, 1, "b1")).ShouldBe(ReplicaApplyResult.Applied);

        (await ReadAsync(replica, a)).ShouldBe((7L, "a7"));
        (await ReadAsync(replica, b)).ShouldBe((1L, "b1"));
    }

    [Fact(DisplayName = "EV-ORDER: the replica write rolls back with the caller's transaction")]
    public async Task RunsInsideCallerTransaction()
    {
        var replica = await CreateReplicaAsync();
        var id = Guid.CreateVersion7();

        await using (var connection = await db.GetServiceDataSource(ServiceRole).OpenConnectionAsync(Ct))
        await using (var transaction = await connection.BeginTransactionAsync(Ct))
        {
            (await replica.ApplyAsync(transaction, id, 1, Values("x"), Ct)).ShouldBe(ReplicaApplyResult.Applied);
            await transaction.RollbackAsync(Ct);
        }

        (await db.ScalarAsync<long>($"SELECT count(*) FROM {Schema}.{replica.Table.Table}")).ShouldBe(0);
    }

    [Fact(DisplayName = "EV-ORDER: a completed transaction is refused (never runs in autocommit)")]
    public async Task CompletedTransaction_Throws()
    {
        var replica = await CreateReplicaAsync();

        await using var connection = await db.GetServiceDataSource(ServiceRole).OpenConnectionAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await transaction.CommitAsync(Ct);

        await Should.ThrowAsync<InvalidOperationException>(
            () => replica.ApplyAsync(transaction, Guid.CreateVersion7(), 1, Values("x"), Ct));
    }

    [Fact(DisplayName = "EV-ORDER: values must cover exactly the declared columns (EV-R4 full state)")]
    public async Task Values_MustMatchDeclaredColumns()
    {
        var replica = await CreateReplicaAsync();
        var id = Guid.CreateVersion7();

        await using var connection = await db.GetServiceDataSource(ServiceRole).OpenConnectionAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);

        var missing = new Dictionary<string, object?> { ["name"] = "x" };
        await Should.ThrowAsync<ArgumentException>(() => replica.ApplyAsync(transaction, id, 1, missing, Ct));

        var unknown = new Dictionary<string, object?>(Values("x")) { ["evil\"; DROP TABLE x; --"] = 1 };
        await Should.ThrowAsync<ArgumentException>(() => replica.ApplyAsync(transaction, id, 1, unknown, Ct));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => replica.ApplyAsync(transaction, id, -1, Values("x"), Ct));
    }

    [Theory(DisplayName = "EV-ORDER: identifiers outside the allow-list are rejected")]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("1starts_with_digit")]
    [InlineData("has space")]
    [InlineData("quote\"d")]
    [InlineData("semi;colon")]
    [InlineData("dot.ted")]
    [InlineData("trailing_newline\n")]
    [InlineData("x; DROP TABLE outbox.message; --")]
    [InlineData("a234567890123456789012345678901234567890123456789012345678901234")] // 64 chars
    public void InvalidIdentifiers_AreRejected(string identifier)
    {
        Should.Throw<ArgumentException>(() => new ReplicaTable(identifier, "t", ["c"]));
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", identifier, ["c"]));
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", "t", [identifier]));
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", "t", ["c"], keyColumn: identifier));
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", "t", ["c"], versionColumn: identifier));
    }

    [Fact(DisplayName = "EV-ORDER: duplicate or colliding column declarations are rejected")]
    public void CollidingColumns_AreRejected()
    {
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", "t", ["c", "c"]));
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", "t", ["id"]));
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", "t", ["aggregate_version"]));
        Should.Throw<ArgumentException>(() => new ReplicaTable("s", "t", ["c"], keyColumn: "k", versionColumn: "k"));
        Should.NotThrow(() => new ReplicaTable("s", "t", ["select", "_x", "a234567890123456789012345678901234567890123456789012345678901"]));
    }

    private static Dictionary<string, object?> Values(string name) =>
        new Dictionary<string, object?>
        {
            ["name"] = name,
            ["labels"] = $$"""{"de":"{{name}}"}""",
            ["is_active"] = true,
        };

    private async Task<ReplicaApplyResult> ApplyAsync(VersionedReplica replica, Guid id, long version, string name)
    {
        await using var connection = await db.GetServiceDataSource(ServiceRole).OpenConnectionAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        var result = await replica.ApplyAsync(transaction, id, version, Values(name), Ct);
        await transaction.CommitAsync(Ct);
        return result;
    }

    private async Task<(long Version, string Name)> ReadAsync(VersionedReplica replica, Guid id)
    {
        await using var command = db.Admin.CreateCommand(
            $"SELECT aggregate_version, name, labels->>'de' FROM {Schema}.{replica.Table.Table} WHERE id = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        reader.Read().ShouldBeTrue();
        var name = reader.GetString(1);
        reader.GetString(2).ShouldBe(name); // the jsonb column was written by the same apply
        return (reader.GetInt64(0), name);
    }

    /// <summary>Scratch <c>*_ref</c> table following the documented contract
    /// (<c>id uuid PRIMARY KEY</c>, <c>aggregate_version bigint NOT NULL</c>).</summary>
    private async Task<VersionedReplica> CreateReplicaAsync()
    {
        var table = $"thing_ref_{Guid.NewGuid():N}";

        await using (var owner = await db.OpenAsOwnerAsync(Ct))
        await using (var command = owner.CreateCommand())
        {
            command.CommandText = $"""
                CREATE SCHEMA IF NOT EXISTS {Schema};
                GRANT USAGE ON SCHEMA {Schema} TO {ServiceRole};
                CREATE TABLE {Schema}.{table} (
                  id                uuid    PRIMARY KEY,
                  aggregate_version bigint  NOT NULL,
                  name              text    NOT NULL,
                  labels            jsonb   NOT NULL,
                  is_active         boolean NOT NULL
                );
                GRANT SELECT, INSERT, UPDATE ON {Schema}.{table} TO {ServiceRole};
                """;
            await command.ExecuteNonQueryAsync(Ct);
        }

        return new VersionedReplica(new ReplicaTable(
            Schema,
            table,
            ["name", new ReplicaColumn("labels", NpgsqlDbType.Jsonb), "is_active"]));
    }
}
