using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Testing;
using Nexus.BuildingBlocks.Tests.Support;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// BE-R1 (conventions-backend.md): every query runs inside the <see cref="UnitOfWork"/>, which
/// fixes <c>app.current_user_id</c> (and <c>app.correlation_id</c>) transaction-locally
/// (conventions-database.md §7). No table has RLS yet in 0.3, so BE-R1 is proved here on the
/// session context itself: it is the user inside the transaction and empty outside it.
///
/// <para>
/// "Outside" is checked on the <b>same physical session</b> the unit of work used (a dedicated
/// one-connection pool, same <c>pg_backend_pid()</c>), with Npgsql's reset-on-close disabled:
/// otherwise a <c>DISCARD ALL</c> issued by the pool on return could hide a session-level
/// (non-LOCAL) <c>set_config</c> leak, and the test would pass for the wrong reason.
/// </para>
/// </summary>
public sealed class UnitOfWorkSessionContextTests(PostgresFixture db)
{
    private const string SessionContextSql =
        "SELECT current_setting('app.current_user_id', true), current_setting('app.correlation_id', true), pg_backend_pid()";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "BE-R1: inside the unit of work app.current_user_id is the user; after COMMIT, on the same session, it is empty")]
    public async Task CurrentUserId_IsSetInsideTransaction_AndEmptyAfterCommit()
    {
        await using var dataSource = SingleSessionDataSource();
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();

        int pidInside;
        await using (var uow = new UnitOfWork(
            dataSource,
            new TestCurrentUser(userId),
            new CorrelationContext { CorrelationId = correlationId },
            UnitOfWorks.RealOutboxWriter()))
        {
            await uow.BeginAsync(Ct);

            var (user, correlation, pid) = await ReadSessionContextAsync(uow.Connection, uow.Transaction);
            user.ShouldBe(userId.ToString("D"));
            correlation.ShouldBe(correlationId.ToString("D"));
            pidInside = pid;

            await uow.CommitAsync(Ct);
        }

        await using var connection = await dataSource.OpenConnectionAsync(Ct);
        var (userAfter, correlationAfter, pidAfter) = await ReadSessionContextAsync(connection, transaction: null);
        pidAfter.ShouldBe(pidInside, "the check must run on the very session the unit of work used");
        userAfter.ShouldBeNullOrEmpty();
        correlationAfter.ShouldBeNullOrEmpty();
    }

    [Fact(DisplayName = "BE-R1: after a rolled-back unit of work, app.current_user_id is empty on the same session")]
    public async Task CurrentUserId_IsEmptyAfterRollback()
    {
        await using var dataSource = SingleSessionDataSource();
        var userId = Guid.CreateVersion7();

        int pidInside;
        await using (var uow = UnitOfWorks.Create(dataSource, userId))
        {
            await uow.BeginAsync(Ct);
            var (user, _, pid) = await ReadSessionContextAsync(uow.Connection, uow.Transaction);
            user.ShouldBe(userId.ToString("D"));
            pidInside = pid;
            await uow.RollbackAsync(Ct);
        }

        await using var connection = await dataSource.OpenConnectionAsync(Ct);
        var (userAfter, _, pidAfter) = await ReadSessionContextAsync(connection, transaction: null);
        pidAfter.ShouldBe(pidInside);
        userAfter.ShouldBeNullOrEmpty();
    }

    [Fact(DisplayName = "BE-R1: two concurrent units of work each see only their own app.current_user_id")]
    public async Task ConcurrentUnitsOfWork_DoNotSeeEachOthersUser()
    {
        var dataSource = db.GetServiceDataSource("svc_platform");
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();

        await using var uowA = UnitOfWorks.Create(dataSource, userA);
        await using var uowB = UnitOfWorks.Create(dataSource, userB);
        await uowA.BeginAsync(Ct);
        await uowB.BeginAsync(Ct);

        (await ReadSessionContextAsync(uowA.Connection, uowA.Transaction)).User.ShouldBe(userA.ToString("D"));
        (await ReadSessionContextAsync(uowB.Connection, uowB.Transaction)).User.ShouldBe(userB.ToString("D"));

        await uowA.CommitAsync(Ct);
        await uowB.CommitAsync(Ct);
    }

    [Fact(DisplayName = "BE-R1: a unit of work refuses to start without an authenticated user (no query runs without app.current_user_id)")]
    public async Task BeginAsync_WithoutAuthenticatedUser_Throws()
    {
        var dataSource = db.GetServiceDataSource("svc_platform");

        await using var anonymous = new UnitOfWork(
            dataSource,
            new TestCurrentUser(Guid.CreateVersion7(), isAuthenticated: false),
            new CorrelationContext { CorrelationId = Guid.CreateVersion7() },
            UnitOfWorks.RealOutboxWriter());
        await Should.ThrowAsync<InvalidOperationException>(() => anonymous.BeginAsync(Ct));
        anonymous.IsActive.ShouldBeFalse();

        await using var emptyUser = UnitOfWorks.Create(dataSource, Guid.Empty);
        await Should.ThrowAsync<InvalidOperationException>(() => emptyUser.BeginAsync(Ct));
        emptyUser.IsActive.ShouldBeFalse();
    }

    /// <summary>One physical session, never reset by the pool: see the class summary.</summary>
    private ServiceDataSource SingleSessionDataSource()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(db.GetConnectionString("svc_platform"))
        {
            MinPoolSize = 0,
            MaxPoolSize = 1,
            NoResetOnClose = true,
        }.ConnectionString;
        return new ServiceDataSource(connectionString);
    }

    private static async Task<(string? User, string? Correlation, int Pid)> ReadSessionContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(SessionContextSql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt32(2));
    }
}
