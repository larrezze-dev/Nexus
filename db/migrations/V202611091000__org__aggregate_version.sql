-- org: persisted aggregate version on every aggregate root table
-- (ADR-0019; conventions-database.md §4 "Tablas raíz de agregado").
--
-- aggregate_version = number of events the aggregate has emitted. The
-- application (UnitOfWork) writes it explicitly, in the same transaction as
-- the outbox.message rows it numbers, and EF Core uses it as a concurrency
-- token (UPDATE ... WHERE aggregate_version = <loaded>). xmin still backs the
-- ETag / If-Match of the API; this column does not replace it.
--
-- Tables: org.currency, org.party, org."group", org.legal_entity,
-- org.business_unit, org.location, org.warehouse, org.app_user -- every table
-- of V202610241000__org__core.sql, all of them aggregate roots.
--
-- How the column is added, per table:
--   1. ADD COLUMN ... NOT NULL DEFAULT 1 -- backfill. Every event already
--      published for these aggregates carries version 1 (ADR-0019 §4), so the
--      next one must go out as 2. On PostgreSQL >= 11 a constant default is
--      stored once in the catalog (pg_attribute.attmissingval): no table
--      rewrite and no UPDATE, hence nothing for triggers or RLS to see (org
--      has neither, but this stays true if that ever changes).
--   2. ALTER COLUMN ... SET DEFAULT 0 -- only affects rows inserted from now
--      on (ADR-0019 §1: a new aggregate has emitted 0 events). Existing rows
--      keep the 1 from step 1. The application always writes the value
--      explicitly; the default only covers SQL inserts that emit no event
--      (seeds, test fixtures), which is why versions can have gaps and
--      consumers compare with ">" only (conventions-events.md §7, §8).
--   3. CHECK (aggregate_version >= 0).
--
-- Grants: none needed. Every grant on these tables (svc_platform DML, other
-- services and svc_solver SELECT, R__access__solver_grants.sql) is
-- table-level, so it already covers the new column. No column-level grants
-- exist on org.
--
-- Ownership (DBC-6): afterConnect.sql runs `SET ROLE nexus_owner`, so the
-- new constraints are owned by nexus_owner like the tables themselves.

-- ---------------------------------------------------------------------------
-- org.currency
-- ---------------------------------------------------------------------------

ALTER TABLE org.currency ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org.currency ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org.currency ADD CONSTRAINT ck_currency_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- org.party
-- ---------------------------------------------------------------------------

ALTER TABLE org.party ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org.party ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org.party ADD CONSTRAINT ck_party_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- org."group"
-- ---------------------------------------------------------------------------

ALTER TABLE org."group" ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org."group" ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org."group" ADD CONSTRAINT ck_group_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- org.legal_entity
-- ---------------------------------------------------------------------------

ALTER TABLE org.legal_entity ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org.legal_entity ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org.legal_entity ADD CONSTRAINT ck_legal_entity_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- org.business_unit
-- ---------------------------------------------------------------------------

ALTER TABLE org.business_unit ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org.business_unit ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org.business_unit ADD CONSTRAINT ck_business_unit_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- org.location
-- ---------------------------------------------------------------------------

ALTER TABLE org.location ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org.location ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org.location ADD CONSTRAINT ck_location_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- org.warehouse
-- ---------------------------------------------------------------------------

ALTER TABLE org.warehouse ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org.warehouse ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org.warehouse ADD CONSTRAINT ck_warehouse_aggregate_version CHECK (aggregate_version >= 0);

-- ---------------------------------------------------------------------------
-- org.app_user
-- ---------------------------------------------------------------------------

ALTER TABLE org.app_user ADD COLUMN aggregate_version bigint NOT NULL DEFAULT 1;
ALTER TABLE org.app_user ALTER COLUMN aggregate_version SET DEFAULT 0;
ALTER TABLE org.app_user ADD CONSTRAINT ck_app_user_aggregate_version CHECK (aggregate_version >= 0);
