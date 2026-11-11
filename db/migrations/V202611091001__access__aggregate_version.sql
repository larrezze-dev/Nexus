-- access: persisted aggregate version on the aggregate root tables
-- (ADR-0019; conventions-database.md §4 "Tablas raíz de agregado").
--
-- aggregate_version = number of events the aggregate has emitted. The
-- application (UnitOfWork) writes it explicitly, in the same transaction as
-- the outbox.message rows it numbers, and EF Core uses it as a concurrency
-- token (UPDATE ... WHERE aggregate_version = <loaded>). xmin still backs the
-- ETag / If-Match of the API; this column does not replace it.
--
-- Tables: access.role, access.role_assignment.
--
-- Excluded on purpose:
--   - access.permission: seeded catalog (db/seed/common), no aggregate class
--     and no events.
--   - access.role_permission: A10 history owned by the Role aggregate. Its
--     rows are never an aggregate of their own; granting/revoking a
--     permission is a Role event and bumps access.role.aggregate_version.
--
-- How the column is added, per table:
--   1. ADD COLUMN ... NOT NULL DEFAULT 1 -- backfill. Every event already
--      published for these aggregates carries version 1 (ADR-0019 §4), so the
--      next one must go out as 2. On PostgreSQL >= 11 a constant default is
--      stored once in the catalog (pg_attribute.attmissingval): no table
--      rewrite and no UPDATE, hence nothing for triggers or RLS to see (access
--      has neither, conventions-database.md §7).
--   2. ALTER COLUMN ... SET DEFAULT 0 -- only affects rows inserted from now
--      on (ADR-0019 §1: a new aggregate has emitted 0 events). Existing rows
--      keep the 1 from step 1. The application always writes the value
--      explicitly; the default only covers SQL inserts that emit no event
--      (seeds, test fixtures), which is why versions can have gaps and
--      consumers compare with ">" only (conventions-events.md §7, §8).
--   3. CHECK (aggregate_version >= 0).
--
-- Grants: none needed. svc_platform's INSERT, UPDATE, SELECT on these tables
-- is table-level (V202610300800__access__core.sql), so it already covers the
-- new column; no other role has any privilege on them. No column-level
-- grants exist on access.
--
-- Ownership (DBC-6): afterConnect.sql runs `SET ROLE nexus_owner`, so the
-- new constraints are owned by nexus_owner like the tables themselves.

-- ---------------------------------------------------------------------------
-- access.role
-- ---------------------------------------------------------------------------

ALTER TABLE access.role ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE access.role ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE access.role ADD CONSTRAINT ck_role_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- access.role_assignment
-- ---------------------------------------------------------------------------

ALTER TABLE access.role_assignment ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE access.role_assignment ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE access.role_assignment ADD CONSTRAINT ck_role_assignment_aggregate_version CHECK (aggregate_version >= 0);
