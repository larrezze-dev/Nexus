// One PostgresFixture (one Postgres container) for the whole assembly, shared by every test
// class: the same [assembly: AssemblyFixture] pattern Nexus.BuildingBlocks.Testing.Tests uses.
// Test classes run in parallel against it, so every test isolates its own data (fresh UUIDv7
// ids and scope keys) and never relies on table-wide counts.
[assembly: AssemblyFixture(typeof(Nexus.BuildingBlocks.Testing.PostgresFixture))]
