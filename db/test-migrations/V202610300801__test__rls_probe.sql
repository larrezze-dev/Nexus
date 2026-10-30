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

-- No GRANT to any svc_* role here, on purpose (corrected after CI caught
-- the regression this caused): V202610231002__test__probe.sql (0.3)
-- deliberately holds `svc_platform`/`svc_catalog` to zero access on schema
-- `test` ("defense in depth" -- see ForbidMutationTests.cs and
-- RolePrivilegeTests.cs' SvcCatalog_CannotInsertIntoForeignSchema, which
-- assert the exact "permission denied for schema test" message). Granting
-- schema USAGE here to make rls_probe reachable would silently change that
-- message for every service role holding it, breaking those exact-match
-- assertions without changing what they're actually testing. RLS itself
-- does not need a service-role grant to be exercised: FORCE ROW LEVEL
-- SECURITY applies to nexus_owner too, so Track B drives this probe via the
-- owner connection with varying `app.current_user_id`, the same pattern
-- ForbidMutationTests.cs already uses for its owner-side checks
-- (RuntimeTestSchema.OpenAsOwnerAsync).
