using Nexus.BuildingBlocks.Testing;
using Npgsql;

namespace Nexus.BuildingBlocks.Tests.Support;

/// <summary>
/// Test-only objects created at run time inside the fixture's throw-away container, never as a
/// versioned migration (ficha 0.3: no <c>number_sequence</c> exists in any 0.3 schema; the first
/// one arrives with a business context, conventions-database.md §11).
///
/// <para>
/// They live in their own schema <c>test_runtime</c>, not in <c>test</c>: the forbid_mutation
/// and role tests rely on service roles having <b>no</b> privilege at all on schema <c>test</c>
/// ("permission denied for schema test"), so granting <c>svc_platform</c> USAGE there would
/// silently change what those tests prove.
/// </para>
///
/// <para>
/// Created by <c>nexus_migrator</c> under <c>SET ROLE nexus_owner</c>, exactly like a Flyway
/// migration would (afterConnect.sql), so ownership and grants match a real environment.
/// <c>svc_platform</c> plays "the service owning this schema" (ARC-R1).
/// </para>
/// </summary>
internal static class RuntimeTestSchema
{
    public const string Schema = "test_runtime";

    public const string OwningServiceRole = "svc_platform";

    private const string Ddl = """
        CREATE SCHEMA test_runtime;

        -- A plain business table, written by "the handler" inside a unit of work.
        CREATE TABLE test_runtime.business_row (
          id   uuid PRIMARY KEY,
          name text NOT NULL
        );

        -- Exact DDL of conventions-database.md §11.
        CREATE TABLE test_runtime.number_sequence (
          scope_key  text PRIMARY KEY,         -- '<DOC>:<le_code>:<yyyy>'
          next_value bigint NOT NULL DEFAULT 1
        );

        REVOKE ALL ON SCHEMA test_runtime FROM PUBLIC;
        GRANT USAGE ON SCHEMA test_runtime TO svc_platform;
        GRANT SELECT, INSERT ON test_runtime.business_row TO svc_platform;
        -- INSERT ... ON CONFLICT DO UPDATE ... RETURNING needs all three.
        GRANT SELECT, INSERT, UPDATE ON test_runtime.number_sequence TO svc_platform;
        """;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgresFixture? _createdFor;

    public static async Task EnsureCreatedAsync(PostgresFixture db, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (ReferenceEquals(_createdFor, db))
            {
                return;
            }

            await using var connection = await OpenAsOwnerAsync(db, cancellationToken);
            await using var command = new NpgsqlCommand(Ddl, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _createdFor = db;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>A connection as <c>nexus_migrator</c> already switched to <c>nexus_owner</c> (the owner of every object).</summary>
    public static async Task<NpgsqlConnection> OpenAsOwnerAsync(PostgresFixture db, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(db.GetConnectionString("nexus_migrator"));
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var setRole = new NpgsqlCommand("SET ROLE nexus_owner", connection);
            await setRole.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
