-- Flyway callback (not a migration: no V/R prefix, so it never enters
-- flyway_schema_history). Runs after every connection Flyway opens.
--
-- nexus_migrator is only a MEMBER of nexus_owner (conventions-database.md §2), not
-- nexus_owner itself, so without this every object a migration creates would end up
-- owned by nexus_migrator and fail DBC-6 in db/ci/checks.sql.
SET ROLE nexus_owner;
