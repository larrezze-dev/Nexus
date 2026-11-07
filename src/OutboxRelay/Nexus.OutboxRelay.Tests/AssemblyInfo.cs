// One real PostgreSQL (roles + Flyway) and one real Kafka + Schema Registry for the whole
// assembly.
[assembly: AssemblyFixture(typeof(Nexus.BuildingBlocks.Testing.PostgresFixture))]
[assembly: AssemblyFixture(typeof(Nexus.OutboxRelay.Tests.Infrastructure.KafkaFixture))]

namespace Nexus.OutboxRelay.Tests;

/// <summary>
/// Every test class that touches outbox.message or the leader lock joins this collection: they
/// share both, so they must run one at a time (xUnit runs the tests of one collection serially).
/// </summary>
[CollectionDefinition(Name)]
public sealed class RelayDatabaseTests
{
    public const string Name = "relay-database";
}
