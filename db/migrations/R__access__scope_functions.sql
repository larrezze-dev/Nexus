-- Scope functions (domain/access.md §4; ADR-0004: evaluated per statement,
-- no materialized view). STABLE SECURITY DEFINER, owned by nexus_owner
-- (afterConnect.sql keeps the Flyway session as nexus_owner), fixed
-- search_path. EXECUTE granted to every svc_* role, including svc_solver
-- (has_permission/current_user_id, used from step 4.4).
--
-- current_user_id() is the one exception: it only reads a session GUC, so it
-- needs neither SECURITY DEFINER nor a fixed search_path -- this matches the
-- literal definition in domain/access.md §4, not an oversight.

CREATE OR REPLACE FUNCTION access.current_user_id() RETURNS uuid
LANGUAGE sql STABLE AS $$
  SELECT nullif(current_setting('app.current_user_id', true), '')::uuid
$$;

CREATE OR REPLACE FUNCTION access.active_assignments()
RETURNS TABLE (assignment_id uuid, role_id uuid, scope_type text, scope_entity_id uuid)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = access, org, pg_temp AS $$
  SELECT ra.id, ra.role_id, ra.scope_type, ra.scope_entity_id
  FROM access.role_assignment ra
  JOIN org.app_user u ON u.id = ra.user_id AND u.is_active
  JOIN access.role r  ON r.id = ra.role_id AND r.is_active
  WHERE ra.user_id = access.current_user_id()
    AND ra.valid_from <= now()
    AND (ra.valid_to IS NULL OR ra.valid_to > now())
$$;

CREATE OR REPLACE FUNCTION access.visible_legal_entities() RETURNS uuid[]
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = access, org, pg_temp AS $$
  WITH a AS (SELECT * FROM access.active_assignments())
  SELECT coalesce(array_agg(le.id), '{}')
  FROM org.legal_entity le
  WHERE EXISTS (SELECT 1 FROM a WHERE a.scope_type = 'GLOBAL')
     OR le.group_id IN (SELECT scope_entity_id FROM a WHERE scope_type = 'GROUP')
     OR le.id       IN (SELECT scope_entity_id FROM a WHERE scope_type = 'LEGAL_ENTITY')
$$;

-- NOTE (deviation from the literal domain/access.md §4 text): the domain
-- doc calls `access.visible_legal_entities()` here as a bare function call
-- inside ANY(...), not wrapped in `(SELECT ...)`. That compiles, but without
-- the (SELECT ...) InitPlan wrapping (same ADR-0004/§7 reasoning as the RLS
-- policy template) Postgres re-evaluates it once per row of
-- org.business_unit/org.warehouse instead of once per statement -- measured
-- at ~22ms with 300 warehouses for a single assignment, vs ~0.3ms wrapped,
-- same result either way. Flagged for the same spec/ fix as the §7 template.
CREATE OR REPLACE FUNCTION access.visible_business_units() RETURNS uuid[]
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = access, org, pg_temp AS $$
  WITH a AS (SELECT * FROM access.active_assignments())
  SELECT coalesce(array_agg(bu.id), '{}')
  FROM org.business_unit bu
  WHERE bu.legal_entity_id = ANY ((SELECT access.visible_legal_entities())::uuid[])
     OR bu.id IN (SELECT scope_entity_id FROM a WHERE scope_type = 'BUSINESS_UNIT')
$$;

CREATE OR REPLACE FUNCTION access.visible_warehouses() RETURNS uuid[]
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = access, org, pg_temp AS $$
  WITH a AS (SELECT * FROM access.active_assignments())
  SELECT coalesce(array_agg(w.id), '{}')
  FROM org.warehouse w
  WHERE w.legal_entity_id = ANY ((SELECT access.visible_legal_entities())::uuid[])
     OR w.business_unit_id IN (SELECT scope_entity_id FROM a WHERE scope_type = 'BUSINESS_UNIT')
     OR w.id IN (SELECT scope_entity_id FROM a WHERE scope_type = 'WAREHOUSE')
$$;

CREATE OR REPLACE FUNCTION access.visible_projects() RETURNS uuid[]
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = access, org, pg_temp AS $$
  SELECT coalesce(array_agg(scope_entity_id), '{}')
  FROM access.active_assignments() WHERE scope_type = 'PROJECT'
$$;

CREATE OR REPLACE FUNCTION access.has_permission(
  p_permission      text,
  p_legal_entity_id uuid DEFAULT NULL,
  p_business_unit_id uuid DEFAULT NULL,
  p_warehouse_id    uuid DEFAULT NULL,
  p_project_id      uuid DEFAULT NULL)
RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = access, org, pg_temp AS $$
  WITH a AS (
    SELECT a.* FROM access.active_assignments() a
    JOIN access.role_permission rp ON rp.role_id = a.role_id
         AND rp.valid_from <= now() AND (rp.valid_to IS NULL OR rp.valid_to > now())
    JOIN access.permission p ON p.id = rp.permission_id AND p.code = p_permission
  ), target AS (
    SELECT p_legal_entity_id AS le,
           (SELECT group_id FROM org.legal_entity WHERE id = p_legal_entity_id) AS grp,
           coalesce(p_business_unit_id,
                    (SELECT business_unit_id FROM org.warehouse WHERE id = p_warehouse_id)) AS bu,
           p_warehouse_id AS wh, p_project_id AS prj
  )
  SELECT EXISTS (
    SELECT 1 FROM a, target t
    WHERE a.scope_type = 'GLOBAL'
       OR (t.le IS NULL AND t.bu IS NULL AND t.wh IS NULL AND t.prj IS NULL)
       OR (a.scope_type = 'GROUP'         AND a.scope_entity_id = t.grp)
       OR (a.scope_type = 'LEGAL_ENTITY'  AND a.scope_entity_id = t.le)
       OR (a.scope_type = 'BUSINESS_UNIT' AND a.scope_entity_id = t.bu)
       OR (a.scope_type = 'WAREHOUSE'     AND a.scope_entity_id = t.wh)
       OR (a.scope_type = 'PROJECT'       AND a.scope_entity_id = t.prj))
$$;

CREATE OR REPLACE VIEW access.effective_access_scope AS
  SELECT 'LEGAL_ENTITY'  AS dimension, unnest(access.visible_legal_entities()) AS entity_id
  UNION ALL SELECT 'BUSINESS_UNIT', unnest(access.visible_business_units())
  UNION ALL SELECT 'WAREHOUSE',     unnest(access.visible_warehouses())
  UNION ALL SELECT 'PROJECT',       unnest(access.visible_projects());

-- ---------------------------------------------------------------------------
-- Privileges
-- ---------------------------------------------------------------------------

REVOKE EXECUTE ON FUNCTION access.current_user_id() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION access.active_assignments() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION access.visible_legal_entities() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION access.visible_business_units() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION access.visible_warehouses() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION access.visible_projects() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION access.has_permission(text, uuid, uuid, uuid, uuid) FROM PUBLIC;

-- svc_platform already has USAGE ON SCHEMA access (granted in
-- V202610300800__access__core.sql); every other business service role only
-- needs it to call these functions, never to query the tables directly.
GRANT USAGE ON SCHEMA access TO
  svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;

GRANT EXECUTE ON FUNCTION access.current_user_id() TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;
GRANT EXECUTE ON FUNCTION access.active_assignments() TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;
GRANT EXECUTE ON FUNCTION access.visible_legal_entities() TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;
GRANT EXECUTE ON FUNCTION access.visible_business_units() TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;
GRANT EXECUTE ON FUNCTION access.visible_warehouses() TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;
GRANT EXECUTE ON FUNCTION access.visible_projects() TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;
GRANT EXECUTE ON FUNCTION access.has_permission(text, uuid, uuid, uuid, uuid) TO
  svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;

GRANT SELECT ON access.effective_access_scope TO svc_platform;
