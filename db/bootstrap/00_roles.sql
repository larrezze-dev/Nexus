-- NEXUS — database bootstrap: roles, PUBLIC privilege lockdown, extensions.
--
-- Run by a superuser only. In local/CI this is the Postgres container's
-- docker-entrypoint-initdb.d mechanism (see infra/compose/postgres/init); in Azure
-- it will be run by Terraform. It is NOT a Flyway migration: Flyway connects as
-- nexus_migrator, which cannot create roles or touch PUBLIC privileges.
--
-- Idempotency: docker-entrypoint-initdb.d scripts only run on first init against an
-- empty data directory, but this script is still written to be safe to re-run (e.g.
-- after `make reset && make up`, or if a superuser re-applies it by hand): role
-- creation is wrapped in DO blocks that swallow duplicate_object, and every other
-- statement here (ALTER ROLE ... PASSWORD, GRANT role membership, REVOKE, CREATE
-- EXTENSION IF NOT EXISTS) is naturally idempotent in Postgres.
--
-- Passwords: never literals. Each LOGIN role's password is read from an environment
-- variable that must be set in the shell psql runs in (the Postgres container already
-- has it, since it comes from the `environment:` block of the same compose service).
-- We use psql's backtick command-substitution form of \set — `\set x `echo
-- "$ENV_VAR"`` — to read the env var into a psql variable, then reference it with the
-- safely-quoted `:'x'` form inside plain (non-DO) statements, which is the idiomatic
-- psql pattern for secrets: it needs no `-v name=value` flags from the caller, only
-- the env var to be present in the process environment. Expected variables:
--   NEXUS_MIGRATOR_PASSWORD        -> nexus_migrator
--   NEXUS_SVC_PLATFORM_PASSWORD    -> svc_platform
--   NEXUS_SVC_CATALOG_PASSWORD     -> svc_catalog
--   NEXUS_SVC_OPERATIONS_PASSWORD  -> svc_operations
--   NEXUS_SVC_COMMERCIAL_PASSWORD  -> svc_commercial
--   NEXUS_SVC_RESOURCES_PASSWORD   -> svc_resources
--   NEXUS_SVC_CONTROLLING_PASSWORD -> svc_controlling
--   NEXUS_SVC_FIELD_PASSWORD       -> svc_field
--   NEXUS_SVC_RELAY_PASSWORD       -> svc_relay
--   NEXUS_SVC_SOLVER_PASSWORD      -> svc_solver

\set ON_ERROR_STOP on

-- =============================================================================
-- 1. nexus_owner — NOLOGIN. Owns every object in the database; Flyway's migrator
--    role (below) is a member of it so migrations can SET ROLE nexus_owner before
--    creating objects, keeping ownership on nexus_owner rather than on whichever
--    login role happened to run the migration.
-- =============================================================================

DO $$
BEGIN
  CREATE ROLE nexus_owner NOLOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

-- =============================================================================
-- 2. nexus_migrator — LOGIN, member of nexus_owner. This is the role Flyway
--    connects as to run migrations.
-- =============================================================================

\set migrator_password `echo "$NEXUS_MIGRATOR_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE nexus_migrator LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE nexus_migrator WITH PASSWORD :'migrator_password';
GRANT nexus_owner TO nexus_migrator;

-- =============================================================================
-- 3. Service roles — LOGIN, one per service, none with BYPASSRLS. RLS policies
--    (created by each schema's migrations) are what keep these roles confined to
--    their own legal entities; BYPASSRLS would defeat that, so it is withheld here
--    on purpose for every service role except svc_solver (see block 4).
-- =============================================================================

\set svc_platform_password `echo "$NEXUS_SVC_PLATFORM_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_platform LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_platform WITH PASSWORD :'svc_platform_password';

\set svc_catalog_password `echo "$NEXUS_SVC_CATALOG_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_catalog LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_catalog WITH PASSWORD :'svc_catalog_password';

\set svc_operations_password `echo "$NEXUS_SVC_OPERATIONS_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_operations LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_operations WITH PASSWORD :'svc_operations_password';

\set svc_commercial_password `echo "$NEXUS_SVC_COMMERCIAL_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_commercial LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_commercial WITH PASSWORD :'svc_commercial_password';

\set svc_resources_password `echo "$NEXUS_SVC_RESOURCES_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_resources LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_resources WITH PASSWORD :'svc_resources_password';

\set svc_controlling_password `echo "$NEXUS_SVC_CONTROLLING_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_controlling LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_controlling WITH PASSWORD :'svc_controlling_password';

\set svc_field_password `echo "$NEXUS_SVC_FIELD_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_field LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_field WITH PASSWORD :'svc_field_password';

\set svc_relay_password `echo "$NEXUS_SVC_RELAY_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_relay LOGIN;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_relay WITH PASSWORD :'svc_relay_password';

-- =============================================================================
-- 4. svc_solver — LOGIN, BYPASSRLS. The Solver (Timefold) only ever reads data
--    (P6: it never writes transactional state; its proposals go through the
--    normal write path like anything else) but it must see rows across every
--    legal entity at once to optimize transport/allocation globally (S9), which
--    row-level security's per-legal-entity policies would otherwise block. It is
--    therefore the one and only service role allowed to bypass RLS.
-- =============================================================================

\set svc_solver_password `echo "$NEXUS_SVC_SOLVER_PASSWORD"`

DO $$
BEGIN
  CREATE ROLE svc_solver LOGIN BYPASSRLS;
EXCEPTION
  WHEN duplicate_object THEN NULL;
END $$;

ALTER ROLE svc_solver WITH PASSWORD :'svc_solver_password' BYPASSRLS;

-- =============================================================================
-- 5. Lock down PUBLIC. Nobody connects or creates objects just by having an
--    account on the cluster; access is granted explicitly, schema by schema, by
--    the migration that creates that schema (DB-R3). CONNECT itself is a
--    database-level privilege, not a schema/table one, so DB-R3 does not cover
--    it; every role that needs to use this database gets it back here, once,
--    explicitly (nexus_owner needs no CONNECT: it is NOLOGIN and never connects
--    directly -- Flyway and every service connect as a role that merely has it
--    granted, or is a member of it).
-- =============================================================================

REVOKE ALL ON DATABASE nexus FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

GRANT CONNECT ON DATABASE nexus TO
  nexus_migrator,
  svc_platform, svc_catalog, svc_operations, svc_commercial,
  svc_resources, svc_controlling, svc_field, svc_relay, svc_solver;

-- nexus_owner (via nexus_migrator's SET ROLE, see db/migrations/afterConnect.sql)
-- still needs two CREATE privileges that REVOKE ALL above took away:
--   - CREATE ON DATABASE nexus: to run `CREATE SCHEMA <context>` at all (schema
--     creation is governed by a database-level privilege, not a schema-level one).
--   - CREATE ON SCHEMA public: Flyway's own flyway_schema_history tracking table
--     lives there, created before any business schema exists.
GRANT CREATE ON DATABASE nexus TO nexus_owner;
GRANT CREATE ON SCHEMA public TO nexus_owner;

-- nexus_migrator also runs db/ci/checks.sql directly (CI job "db"), which loads
-- its two allow-lists into CREATE TEMP TABLEs; TEMP is bundled into the "ALL" that
-- was just revoked from PUBLIC, so it needs to come back explicitly here too.
GRANT TEMP ON DATABASE nexus TO nexus_migrator;

-- =============================================================================
-- 6. Extensions shared across schemas.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
