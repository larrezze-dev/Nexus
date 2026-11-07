-- Partial index on outbox.message for FAILED rows (step 0.7, relay ordering guard).
--
-- The relay (svc_relay, step 0.7 track B) holds back an aggregate while an
-- earlier event of that same aggregate is FAILED (EV-R3: per-aggregate order).
-- Its batch query is an anti-join:
--
--   NOT EXISTS (SELECT 1 FROM outbox.message f
--               WHERE f.status = 'FAILED'
--                 AND f.aggregate_id = m.aggregate_id
--                 AND (f.created_at, f.id) < (m.created_at, m.id))
--
-- Without an index on FAILED rows the planner must seq-scan outbox.message to
-- find them, and that table keeps every PUBLISHED row for 7 days. FAILED rows
-- are rare, so this partial index stays tiny and lets the planner read only the
-- FAILED set instead of the whole table: with few FAILED rows it reads them
-- from the index once and joins against that small set; with many it can
-- probe the index per candidate on (aggregate_id), resolving the
-- (created_at, id) row comparison inside the same index.
--
-- V202610231001__infra__outbox_inbox.sql is merged and is not edited (ADR-0001).
-- No CONCURRENTLY: Flyway runs each migration in a transaction, and the outbox
-- is small and short-lived, so the brief SHARE lock of a plain CREATE INDEX is
-- acceptable.
--
-- Runs as nexus_migrator with `SET ROLE nexus_owner` (afterConnect.sql); an
-- index always belongs to its table's owner (nexus_owner, DBC-6). Indexes carry
-- no privileges of their own, so grants are unchanged.

CREATE INDEX ix_outbox_failed_aggregate
  ON outbox.message (aggregate_id, created_at, id)
  WHERE status = 'FAILED';
