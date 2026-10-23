-- infra base: schema + append-only guard function.
--
-- Ownership (DBC-6): db/migrations/afterConnect.sql (a Flyway callback, not a
-- migration) already runs `SET ROLE nexus_owner` once for the whole session
-- before any migration executes, so every object created below is owned by
-- nexus_owner. This file must not RESET ROLE: that would undo the switch for
-- every migration that runs after it in the same Flyway invocation.

CREATE SCHEMA infra;

-- Generic trigger function used by `BEFORE UPDATE OR DELETE` triggers on
-- every append-only table in the project (e.g. stock.stock_movement).
-- outbox.message is a separate case and is NOT wired to this function.
CREATE FUNCTION infra.forbid_mutation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'append-only table %', TG_TABLE_NAME USING ERRCODE = 'P0001';
END;
$$ LANGUAGE plpgsql;

-- Schema-level USAGE: required for any role that will reference
-- infra.forbid_mutation() in a trigger definition on its own tables.
GRANT USAGE ON SCHEMA infra TO
  svc_platform,
  svc_catalog,
  svc_operations,
  svc_commercial,
  svc_resources,
  svc_controlling,
  svc_field,
  svc_relay;

-- EXECUTE on the function itself: needed by the service roles that will
-- attach it as a trigger on their append-only tables.
GRANT EXECUTE ON FUNCTION infra.forbid_mutation() TO
  svc_platform,
  svc_catalog,
  svc_operations,
  svc_commercial,
  svc_resources,
  svc_controlling,
  svc_field;
