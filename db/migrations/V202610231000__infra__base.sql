-- infra base: schema + append-only guard function.
--
-- Ownership note (DBC-6): Flyway connects as `nexus_migrator`, a login role
-- that is a MEMBER of `nexus_owner` (NOLOGIN). Role membership only grants
-- inherited privileges; it does NOT make objects created by the member be
-- owned by the parent role. Without an explicit `SET ROLE`, every object
-- created in this session would be owned by `nexus_migrator`, which would
-- fail DBC-6 ("table owned by a role other than nexus_owner"). We therefore
-- switch the current role to `nexus_owner` for the duration of this
-- migration and reset it at the end.
SET ROLE nexus_owner;

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

RESET ROLE;
