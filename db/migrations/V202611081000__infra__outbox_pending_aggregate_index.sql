-- Partial index on outbox.message for PENDING rows by aggregate (step 0.7,
-- relay unreachable-topic guard).
--
-- When a Kafka topic is unreachable, the relay (svc_relay, step 0.7 track B)
-- skips every PENDING row on that topic, and also every later PENDING row of an
-- aggregate that already has an earlier PENDING row on that topic (EV-R3:
-- per-aggregate order, even when the aggregate's events span several topics).
-- Its batch query ($2 = text[] of unreachable topics) adds:
--
--   AND NOT (m.topic = ANY($2))
--   AND NOT EXISTS (SELECT 1 FROM outbox.message p
--                   WHERE p.status = 'PENDING'
--                     AND p.aggregate_id = m.aggregate_id
--                     AND p.topic = ANY($2)
--                     AND (p.created_at, p.id) < (m.created_at, m.id))
--
-- ix_outbox_pending (created_at, id) serves the batch order but cannot find the
-- PENDING rows of one aggregate; without this index the anti-join would scan
-- every PENDING row for each candidate, or hash the whole PENDING set. With it
-- the planner probes on (aggregate_id) and resolves the (created_at, id) row
-- comparison inside the same index, then checks topic on the heap row.
--
-- Write cost: one more index entry per insert, and one more index update when
-- a row leaves PENDING. It is partial, so PUBLISHED and FAILED rows drop out of
-- it and it stays the size of the backlog, not of the 7-day table.
--
-- Merged migrations are not edited (ADR-0001), so this is a new migration.
-- No CONCURRENTLY: Flyway runs each migration in a transaction, and the outbox
-- is small and short-lived, so the brief SHARE lock of a plain CREATE INDEX is
-- acceptable.
--
-- Runs as nexus_migrator with `SET ROLE nexus_owner` (afterConnect.sql); an
-- index always belongs to its table's owner (nexus_owner, DBC-6). Indexes carry
-- no privileges of their own, so grants are unchanged.

CREATE INDEX ix_outbox_pending_aggregate
  ON outbox.message (aggregate_id, created_at, id)
  WHERE status = 'PENDING';
