-- RLS policy template probe (conventions-database.md §7; step 0.6).
--
-- test.rls_probe exercises the full LE+BU+WH+PRJ disjunctive policy profile
-- against two Mandanten, before any real domain table adopts it.
-- legal_entity_id is NOT NULL (the "siempre" dimension); business_unit_id/
-- warehouse_id/project_id are optional narrower scopes layered on top,
-- exactly like a real multi-dimensional table.
--
-- No FKs to org here on purpose: this is a synthetic fixture, so tests can
-- insert arbitrary ids for LE/BU/WH/PROJECT without needing matching real
-- org rows.
--
-- The policy itself is NOT in this file: conventions-database.md §7 says
-- "las políticas van en migraciones repetibles R__<contexto>__rls.sql", and
-- Flyway always applies every pending VERSIONED migration before any
-- REPEATABLE one -- so a policy here, referencing the access.visible_*()
-- functions defined in the repeatable R__access__scope_functions.sql, would
-- fail on a clean database with "function ... does not exist" (reproduced
-- against Flyway 13.11.1). See db/test-migrations/R__test__rls.sql.

CREATE TABLE test.rls_probe (
  id               uuid PRIMARY KEY,
  legal_entity_id  uuid NOT NULL,
  business_unit_id uuid NULL,
  warehouse_id     uuid NULL,
  project_id       uuid NULL,
  payload          text NOT NULL
);

ALTER TABLE test.rls_probe ENABLE ROW LEVEL SECURITY;
ALTER TABLE test.rls_probe FORCE  ROW LEVEL SECURITY;

-- USAGE ON SCHEMA test was never granted to any service role by
-- V202610231002__test__probe.sql (0.3) either -- table-level grants alone
-- are not enough to reach a table in a schema without USAGE. Granted here,
-- scoped to this file, rather than touching the already-applied 0.3 file.
GRANT USAGE ON SCHEMA test TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field;

GRANT INSERT, SELECT, UPDATE, DELETE ON test.rls_probe TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field;
