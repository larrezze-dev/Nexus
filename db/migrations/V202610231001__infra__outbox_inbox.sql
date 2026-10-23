-- Outbox / Inbox infrastructure (conventions-events.md §3, §4, §7; step 0.3).
--
-- outbox.message: every business service writes its events here in the same
-- transaction as the business change (via UnitOfWork). The relay (svc_relay,
-- step 0.7) reads PENDING rows, publishes them to Kafka, updates only the
-- status columns and purges PUBLISHED rows older than 7 days. The purge
-- window is enforced by the relay logic, not by the database.
--
-- inbox.processed_message: consumer-side idempotency (one row per consumer
-- and event_id).
--
-- No RLS on outbox/inbox (conventions-database.md §2). Not append-only: the
-- relay mutates status columns, so infra.forbid_mutation() is NOT attached.
--
-- Runs as nexus_migrator (member of nexus_owner). Ownership is transferred to
-- nexus_owner before granting, so every ACL entry is recorded with
-- nexus_owner as grantor (DBC-6).

-- ---------------------------------------------------------------------------
-- outbox
-- ---------------------------------------------------------------------------

CREATE SCHEMA outbox;

CREATE TABLE outbox.message (
  id                uuid PRIMARY KEY,                 -- = event_id
  topic             text        NOT NULL,
  aggregate_type    text        NOT NULL,
  aggregate_id      uuid        NOT NULL,
  aggregate_version bigint      NOT NULL,
  event_type        text        NOT NULL,
  schema_version    integer     NOT NULL,
  headers           jsonb       NOT NULL,             -- rest of the envelope
  payload           jsonb       NOT NULL,             -- Avro in JSON encoding
  created_at        timestamptz NOT NULL,
  published_at      timestamptz NULL,
  status            text        NOT NULL DEFAULT 'PENDING'
                    CHECK (status IN ('PENDING','PUBLISHED','FAILED')),
  attempts          integer     NOT NULL DEFAULT 0,
  last_error        text        NULL
);
CREATE INDEX ix_outbox_pending ON outbox.message (created_at, id) WHERE status = 'PENDING';

-- Wake up the relay on every new outbox row. Empty payload: the relay
-- re-queries ix_outbox_pending; NOTIFY is only a hint (it also polls).
CREATE FUNCTION outbox.notify_outbox_new()
  RETURNS trigger
  LANGUAGE plpgsql
  SECURITY INVOKER
  SET search_path = pg_catalog
AS $$
BEGIN
  PERFORM pg_notify('outbox_new', '');
  RETURN NULL;
END;
$$;

CREATE TRIGGER trg_outbox_message_notify
  AFTER INSERT ON outbox.message
  FOR EACH ROW
  EXECUTE FUNCTION outbox.notify_outbox_new();

-- ---------------------------------------------------------------------------
-- inbox
-- ---------------------------------------------------------------------------

CREATE SCHEMA inbox;

CREATE TABLE inbox.processed_message (
  consumer     text        NOT NULL,
  event_id     uuid        NOT NULL,
  processed_at timestamptz NOT NULL,
  PRIMARY KEY (consumer, event_id)
);

-- ---------------------------------------------------------------------------
-- Ownership (DBC-6). Indexes and the trigger follow their table's owner.
-- ---------------------------------------------------------------------------

ALTER SCHEMA outbox OWNER TO nexus_owner;
ALTER SCHEMA inbox OWNER TO nexus_owner;
ALTER TABLE outbox.message OWNER TO nexus_owner;
ALTER TABLE inbox.processed_message OWNER TO nexus_owner;
ALTER FUNCTION outbox.notify_outbox_new() OWNER TO nexus_owner;

-- ---------------------------------------------------------------------------
-- Privileges (exact; no more, no less)
-- ---------------------------------------------------------------------------

-- Trigger function: never callable directly; EXECUTE is not checked when the
-- trigger fires, so revoking the default PUBLIC grant is pure hardening.
REVOKE ALL ON FUNCTION outbox.notify_outbox_new() FROM PUBLIC;

REVOKE ALL ON SCHEMA outbox FROM PUBLIC;
REVOKE ALL ON SCHEMA inbox FROM PUBLIC;
REVOKE ALL ON TABLE outbox.message FROM PUBLIC;
REVOKE ALL ON TABLE inbox.processed_message FROM PUBLIC;

-- Business services: write-only into the outbox.
GRANT USAGE ON SCHEMA outbox
  TO svc_platform, svc_catalog, svc_operations, svc_commercial,
     svc_resources, svc_controlling, svc_field;
GRANT INSERT ON TABLE outbox.message
  TO svc_platform, svc_catalog, svc_operations, svc_commercial,
     svc_resources, svc_controlling, svc_field;

-- Relay: read, update status columns only, purge. No INSERT.
GRANT USAGE ON SCHEMA outbox TO svc_relay;
GRANT SELECT ON TABLE outbox.message TO svc_relay;
GRANT UPDATE (status, published_at, attempts, last_error) ON TABLE outbox.message TO svc_relay;
GRANT DELETE ON TABLE outbox.message TO svc_relay;

-- Business services: consumer idempotency.
GRANT USAGE ON SCHEMA inbox
  TO svc_platform, svc_catalog, svc_operations, svc_commercial,
     svc_resources, svc_controlling, svc_field;
GRANT SELECT, INSERT ON TABLE inbox.processed_message
  TO svc_platform, svc_catalog, svc_operations, svc_commercial,
     svc_resources, svc_controlling, svc_field;
