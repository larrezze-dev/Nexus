-- RLS policy for test.rls_probe (conventions-database.md §7; step 0.6).
--
-- Repeatable on purpose (conventions-database.md §7: "las políticas van en
-- migraciones repetibles R__<contexto>__rls.sql"): Flyway applies every
-- pending VERSIONED migration before any REPEATABLE one, so this cannot
-- live in the same V file as the table (db/test-migrations/
-- V202610300801__test__rls_probe.sql) without racing the creation of the
-- access.visible_*() functions it calls (also repeatable, in
-- db/migrations/R__access__scope_functions.sql). Flyway sorts repeatable
-- migrations by description, so "access  scope functions" runs before
-- "test  rls" -- confirmed against Flyway 13.11.1.
--
-- NOTE (deviation from the literal conventions-database.md §7 template):
-- `col = ANY ((SELECT func()))` does not type-check in real PostgreSQL when
-- func() returns an array: a bare "(SELECT ...)" inside ANY(...) is parsed
-- as the sub-SELECT form of ANY (which compares against each row of a
-- *scalar* column), not the array form -- so Postgres looks for a
-- "uuid = uuid[]" operator and fails. Verified empirically against PG17.
-- Casting the scalar subquery to its array type keeps it from being parsed
-- as a sub-SELECT (confirmed via EXPLAIN: still a single InitPlan, i.e.
-- evaluated once per statement, matching the ADR-0004/§7 intent) while
-- fixing the type: `(SELECT func())::uuid[]`. Flagged for a spec/ fix to
-- conventions-database.md §7 and domain/access.md §4's own template text.

DROP POLICY IF EXISTS rls_rls_probe ON test.rls_probe;
CREATE POLICY rls_rls_probe ON test.rls_probe
  USING (
    legal_entity_id      = ANY ((SELECT access.visible_legal_entities())::uuid[])
    OR business_unit_id  = ANY ((SELECT access.visible_business_units())::uuid[])
    OR warehouse_id       = ANY ((SELECT access.visible_warehouses())::uuid[])
    OR project_id         = ANY ((SELECT access.visible_projects())::uuid[])
  )
  WITH CHECK (
    legal_entity_id      = ANY ((SELECT access.visible_legal_entities())::uuid[])
    OR business_unit_id  = ANY ((SELECT access.visible_business_units())::uuid[])
    OR warehouse_id       = ANY ((SELECT access.visible_warehouses())::uuid[])
    OR project_id         = ANY ((SELECT access.visible_projects())::uuid[])
  );
