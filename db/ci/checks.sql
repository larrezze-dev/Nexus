-- NEXUS CI database checks (DBC-1 .. DBC-6).
--
-- Each query below must return ZERO rows when the database is healthy.
-- The CI "db" job runs this script with:
--
--     psql -f db/ci/checks.sql
--
-- from the repository root (so the relative paths to
-- db/ci/rls-exceptions.txt and db/ci/append-only.txt resolve), against
-- a database that has already been migrated with Flyway. If any of
-- the SELECTs below returns one or more rows, the job must fail.
-- Every result row carries a "check_id" column (DBC-1 .. DBC-6) as its
-- first column, and each query is preceded by a \echo so the failing
-- check is unambiguous in the CI log even when several checks fail at
-- once.

\set ON_ERROR_STOP on
\pset pager off

-- ---------------------------------------------------------------------
-- Load the two allow-lists (rls-exceptions.txt, append-only.txt) into
-- temp tables. Both files use the same format: one "schema.table" per
-- line, blank lines and lines starting with "#" are comments and are
-- stripped client-side with awk before COPY ever sees them.
-- ---------------------------------------------------------------------

CREATE TEMP TABLE rls_exceptions_raw (qualified_name text);
\copy rls_exceptions_raw FROM PROGRAM 'awk ''!/^[[:space:]]*#/ && NF>0 {print $1}'' db/ci/rls-exceptions.txt'

CREATE TEMP TABLE rls_exceptions AS
SELECT split_part(qualified_name, '.', 1) AS schema_name,
       split_part(qualified_name, '.', 2) AS table_name
FROM rls_exceptions_raw;

CREATE TEMP TABLE append_only_raw (qualified_name text);
\copy append_only_raw FROM PROGRAM 'awk ''!/^[[:space:]]*#/ && NF>0 {print $1}'' db/ci/append-only.txt'

CREATE TEMP TABLE append_only_tables AS
SELECT split_part(qualified_name, '.', 1) AS schema_name,
       split_part(qualified_name, '.', 2) AS table_name
FROM append_only_raw;

-- ---------------------------------------------------------------------
-- DBC-1: every table that has a "legal_entity_id" column must have
-- ENABLE ROW LEVEL SECURITY and FORCE ROW LEVEL SECURITY (conventions-
-- database.md §7), unless it is listed in db/ci/rls-exceptions.txt.
-- ---------------------------------------------------------------------

\echo 'Checking DBC-1: legal_entity_id tables without RLS enabled+forced ...'
SELECT 'DBC-1' AS check_id,
       n.nspname AS schema_name,
       c.relname AS table_name,
       c.relrowsecurity AS rls_enabled,
       c.relforcerowsecurity AS rls_forced
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE a.attname = 'legal_entity_id'
  AND a.attnum > 0
  AND NOT a.attisdropped
  AND c.relkind IN ('r', 'p')
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
  AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity)
  AND NOT EXISTS (
    SELECT 1 FROM rls_exceptions e
    WHERE e.schema_name = n.nspname
      AND e.table_name = c.relname
  )
ORDER BY 2, 3;

-- ---------------------------------------------------------------------
-- DBC-2: a foreign key whose destination table lives in a different
-- schema than the origin table is only allowed when the destination
-- schema is one of the reference schemas org, uom, catalog (ARC-R2).
-- ---------------------------------------------------------------------

\echo 'Checking DBC-2: cross-schema FKs outside org/uom/catalog ...'
SELECT 'DBC-2' AS check_id,
       ns_o.nspname AS origin_schema,
       c_o.relname AS origin_table,
       ns_d.nspname AS dest_schema,
       c_d.relname AS dest_table,
       con.conname AS constraint_name
FROM pg_constraint con
JOIN pg_class c_o ON c_o.oid = con.conrelid
JOIN pg_namespace ns_o ON ns_o.oid = c_o.relnamespace
JOIN pg_class c_d ON c_d.oid = con.confrelid
JOIN pg_namespace ns_d ON ns_d.oid = c_d.relnamespace
WHERE con.contype = 'f'
  AND ns_o.nspname <> ns_d.nspname
  AND ns_d.nspname NOT IN ('org', 'uom', 'catalog')
ORDER BY 2, 3;

-- ---------------------------------------------------------------------
-- DBC-3: the first column of every foreign key (conkey[1]) must be
-- covered by an index that starts with that same column, otherwise
-- lookups and FK-triggered checks degrade to sequential scans.
-- Note: pg_index.indkey is an int2vector, which (unlike a regular
-- array) is zero-based, so its first element is indkey[0].
-- ---------------------------------------------------------------------

\echo 'Checking DBC-3: FK first columns without a matching leading index ...'
SELECT 'DBC-3' AS check_id,
       n.nspname AS schema_name,
       c.relname AS table_name,
       a.attname AS fk_column,
       con.conname AS constraint_name
FROM pg_constraint con
JOIN pg_class c ON c.oid = con.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = con.conkey[1]
WHERE con.contype = 'f'
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
  AND NOT EXISTS (
    SELECT 1
    FROM pg_index i
    WHERE i.indrelid = con.conrelid
      AND i.indkey[0] = con.conkey[1]
  )
ORDER BY 2, 3;

-- ---------------------------------------------------------------------
-- DBC-4: every table listed in db/ci/append-only.txt must have the
-- "trg_<table>_append_only" trigger (infra.forbid_mutation()).
--
-- These tables are the canonical append-only list from
-- conventions-database.md §6, but most of them do not exist yet in
-- step 0.3 -- they get created incrementally by later steps. To avoid
-- failing prematurely on tables that simply don't exist yet, this
-- check only evaluates append-only.txt entries that already resolve
-- to a real table in pg_class (the join to pg_class acts as the
-- "LEFT JOIN ... WHERE oid IS NOT NULL" filter): a missing table is
-- silently skipped, a table that exists without its trigger fails.
-- ---------------------------------------------------------------------

\echo 'Checking DBC-4: existing append-only tables missing their trigger ...'
SELECT 'DBC-4' AS check_id,
       ao.schema_name,
       ao.table_name,
       'trg_' || ao.table_name || '_append_only' AS expected_trigger
FROM append_only_tables ao
JOIN pg_namespace n ON n.nspname = ao.schema_name
JOIN pg_class c ON c.relnamespace = n.oid
               AND c.relname = ao.table_name
               AND c.relkind IN ('r', 'p')
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_trigger t
    WHERE t.tgrelid = c.oid
      AND t.tgname = 'trg_' || ao.table_name || '_append_only'
      AND NOT t.tgisinternal
  )
ORDER BY 2, 3;

-- ---------------------------------------------------------------------
-- DBC-5: forbidden column types anywhere in the database: timestamp
-- without time zone, real, double precision, money. Dates/times must
-- be timestamptz, money/quantities must be numeric (CLAUDE.md §3).
-- ---------------------------------------------------------------------

\echo 'Checking DBC-5: forbidden column types (timestamp w/o tz, real, double precision, money) ...'
SELECT 'DBC-5' AS check_id,
       table_schema AS schema_name,
       table_name,
       column_name,
       data_type
FROM information_schema.columns
WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
  AND data_type IN ('timestamp without time zone', 'real', 'double precision', 'money')
ORDER BY 2, 3, 4;

-- ---------------------------------------------------------------------
-- DBC-6: every table outside system schemas must be owned by
-- nexus_owner, so the application role set and migrations stay
-- consistent with a single owning role per environment. Session-local
-- temp schemas (pg_temp_*, pg_toast_temp_*) are excluded: they hold
-- this very script's own scratch tables (rls_exceptions, ...), owned
-- by whichever role runs psql, not by application migrations.
-- ---------------------------------------------------------------------

\echo 'Checking DBC-6: tables not owned by nexus_owner ...'
SELECT 'DBC-6' AS check_id,
       schemaname AS schema_name,
       tablename AS table_name,
       tableowner
FROM pg_tables
WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
  AND schemaname NOT LIKE 'pg_temp_%'
  AND schemaname NOT LIKE 'pg_toast_temp_%'
  AND tableowner <> 'nexus_owner'
ORDER BY 2, 3;
