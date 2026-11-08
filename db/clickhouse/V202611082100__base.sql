-- ClickHouse base schema for nexus_analytics (conventions-events.md §8, step 0.8).
--
-- Applied by nexus-projections at startup, which records each file in
-- nexus_analytics._schema_migrations (see db/clickhouse/README.md).
--
-- Bootstrap decision: this file creates the database AND the control table
-- itself, so it is the single source of truth for the _schema_migrations DDL.
-- The applier connects to the `default` database and, when
-- nexus_analytics._schema_migrations does not exist yet, treats the history
-- as empty and runs this file first. Locally the compose service already
-- creates the database (CLICKHOUSE_DB), in other environments it may not, so
-- CREATE DATABASE IF NOT EXISTS is kept here.
--
-- Idempotency: ClickHouse DDL is not transactional. If the applier fails
-- halfway through this file the version is not recorded and the whole file
-- runs again on the next start. Every statement therefore uses IF NOT EXISTS
-- so a re-run resumes cleanly. On a fresh database it behaves exactly like
-- the plain CREATE statements.
--
-- Concurrent starts: two replicas of nexus-projections, or the service and a
-- `rebuild` run, may apply the same pending version at the same time. The
-- statements are re-runnable, so both succeed and both record the version.
-- _schema_migrations is therefore ReplacingMergeTree(applied_at) ORDER BY
-- version: duplicate rows for one version collapse to one, and the history
-- is always read with FINAL. This table is an exception to the
-- legal_entity_id / _aggregate_version columns of §8: it is platform
-- metadata, not a projection.
--
-- Statement splitting: the file contains no semicolon other than the
-- statement terminators (none in comments or literals), so an applier that
-- splits on the terminator is safe.
--
-- Engine and key choices
-- ----------------------
-- event_log: copied verbatim from the step card (columns, types, engine,
--   ORDER BY). ReplacingMergeTree(_ingested_at) collapses re-deliveries of
--   the same event (same aggregate_type, aggregate_id, aggregate_version,
--   event_id) to one row, keeping the latest ingestion. legal_entity_id is
--   Nullable because platform-level events carry an empty
--   nexus-legal-entity-id header (conventions-events.md §2).
--
-- dim_legal_entity / dim_warehouse: one row per aggregate, latest state.
--   ReplacingMergeTree(_aggregate_version): the dedup key is the ORDER BY
--   tuple and the surviving row is the one with the highest
--   _aggregate_version (nexus-aggregate-version header, grows per
--   aggregate). This gives both idempotency (re-processing the same event
--   inserts an identical row with the same version) and ordering (an
--   out-of-order older version never wins over a newer one, which a consumer
--   would otherwise have to check by hand, conventions-events.md §7).
--   Every org.*.created / .updated / .deactivated event carries the full
--   state (EV-R4), so each event maps to one full row insert.
--   Deactivation is a soft state: is_active = false in a new row version.
--   Nothing is ever deleted from these tables (the ReplacingMergeTree
--   is_deleted parameter is not used).
--   Merges are asynchronous, so readers must use FINAL or aggregate with
--   argMax(<col>, _aggregate_version) grouped by the key.
--   ORDER BY starts with legal_entity_id as §8 requires:
--   - dim_legal_entity: legal_entity_id IS the aggregate id, so the key is
--     (legal_entity_id) alone.
--   - dim_warehouse: (legal_entity_id, warehouse_id). Safe because a
--     warehouse never changes legal entity: UpdateWarehouseRequest in
--     contracts/openapi/platform.yaml has no legalEntityId and the domain
--     aggregate never mutates it. If that ever changes, this key would leave
--     a stale row under the old legal entity and needs a new migration.
--   LowCardinality is used for small closed vocabularies (currency, country,
--   i18n language keys). code stays plain String: it is unique per row, so a
--   dictionary would add nothing.

CREATE DATABASE IF NOT EXISTS nexus_analytics;

-- ---------------------------------------------------------------------------
-- Control table for applied ClickHouse migrations
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS nexus_analytics._schema_migrations (
  version     String,
  description String,
  checksum    String,
  applied_at  DateTime64(3, 'UTC')
) ENGINE = ReplacingMergeTree(applied_at)
ORDER BY version;

-- ---------------------------------------------------------------------------
-- event_log: history of every domain event, feeds the audit trail
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS nexus_analytics.event_log (
  legal_entity_id Nullable(UUID), topic LowCardinality(String), event_id UUID, event_type LowCardinality(String),
  aggregate_type LowCardinality(String), aggregate_id UUID, aggregate_version UInt64,
  occurred_at DateTime64(6,'UTC'), actor_user_id UUID, correlation_id UUID, payload_json String,
  _ingested_at DateTime64(3,'UTC')
) ENGINE = ReplacingMergeTree(_ingested_at)
ORDER BY (aggregate_type, aggregate_id, aggregate_version, event_id);
-- Documented exception to conventions-events §8: access is by aggregate, not by legal entity.
-- The scope filter is still applied in the query (legal_entity_id IN (...) OR legal_entity_id IS NULL with GLOBAL permission).

-- ---------------------------------------------------------------------------
-- dim_legal_entity: latest state of each legal entity (nexus.org.legal-entity.*)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS nexus_analytics.dim_legal_entity (
  legal_entity_id    UUID,                    -- = LegalEntity id (Avro id)
  party_id           UUID,
  group_id           UUID,
  code               String,
  currency_code      LowCardinality(String),
  country_code       LowCardinality(String),
  is_active          Bool,
  _event_id          UUID,
  _aggregate_version UInt64,
  _ingested_at       DateTime64(3, 'UTC')
) ENGINE = ReplacingMergeTree(_aggregate_version)
ORDER BY (legal_entity_id);

-- ---------------------------------------------------------------------------
-- dim_warehouse: latest state of each warehouse (nexus.org.warehouse.*)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS nexus_analytics.dim_warehouse (
  legal_entity_id    UUID,                    -- immutable for a warehouse
  warehouse_id       UUID,                    -- = Warehouse id (Avro id)
  location_id        UUID,
  business_unit_id   Nullable(UUID),
  code               String,
  caption_i18n       Map(LowCardinality(String), String),
  is_active          Bool,
  _event_id          UUID,
  _aggregate_version UInt64,
  _ingested_at       DateTime64(3, 'UTC')
) ENGINE = ReplacingMergeTree(_aggregate_version)
ORDER BY (legal_entity_id, warehouse_id);
