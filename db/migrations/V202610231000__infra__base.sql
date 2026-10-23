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

-- No GRANT to any svc_* role here, on purpose: no service role owns tables
-- (DB-R1), so none of them ever runs CREATE TRIGGER -- every append-only
-- trigger is created by nexus_owner, via a Flyway migration (the afterConnect
-- callback keeps the session as nexus_owner throughout). Firing an existing
-- trigger at runtime does not require the invoking role to have EXECUTE on
-- the trigger function (verified empirically: a role with only UPDATE on the
-- table, and no EXECUTE on the function, still gets blocked by the trigger).
-- EXECUTE also stays revoked from PUBLIC, matching outbox.notify_outbox_new().
REVOKE EXECUTE ON FUNCTION infra.forbid_mutation() FROM PUBLIC;
