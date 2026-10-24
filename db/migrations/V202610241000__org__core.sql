-- org core: Party, Group, Legal Entity, Business Unit, Location, Warehouse,
-- App User, and the Currency reference table they depend on
-- (docs/domain/org.md §3; step 0.4).
--
-- org is a reference schema (ARC-R2): any service may read it, but only
-- svc_platform writes to it. No RLS here: org is GLOBAL (Luciano's decision,
-- also resolving ORG-Q1 for org.party's visibility).
--
-- Ownership (DBC-6): db/migrations/afterConnect.sql already runs
-- `SET ROLE nexus_owner` for the whole Flyway session, so every object
-- created below is owned by nexus_owner without any explicit ALTER ... OWNER.
--
-- Standard columns (conventions-database.md §4, "-- + std" in the domain
-- doc): created_at/created_by/updated_at/updated_by are set by the
-- application via TimeProvider, never by a column DEFAULT.

CREATE SCHEMA org;

-- ---------------------------------------------------------------------------
-- org.currency
-- ---------------------------------------------------------------------------

CREATE TABLE org.currency (
  code         char(3)     PRIMARY KEY CHECK (code ~ '^[A-Z]{3}$'),
  caption_i18n jsonb       NOT NULL CHECK (jsonb_typeof(caption_i18n) = 'object' AND caption_i18n ? 'de'),
  minor_units  smallint    NOT NULL DEFAULT 2,
  is_active    boolean     NOT NULL DEFAULT true,
  created_at   timestamptz NOT NULL,
  created_by   uuid        NOT NULL,
  updated_at   timestamptz NOT NULL,
  updated_by   uuid        NOT NULL
);

-- ---------------------------------------------------------------------------
-- org.party
-- ---------------------------------------------------------------------------

CREATE TABLE org.party (
  id               uuid        PRIMARY KEY,
  party_type       text        NOT NULL CHECK (party_type IN ('ORGANIZATION','PERSON')),
  legal_name       text        NOT NULL,
  display_name     text        NULL,
  tax_id           text        NULL,
  erp_company_code text        NULL,
  is_internal      boolean     NOT NULL DEFAULT false,
  is_active        boolean     NOT NULL DEFAULT true,
  created_at       timestamptz NOT NULL,
  created_by       uuid        NOT NULL,
  updated_at       timestamptz NOT NULL,
  updated_by       uuid        NOT NULL
);
CREATE INDEX ix_party_legal_name_trgm ON org.party USING gin (legal_name gin_trgm_ops);

-- ---------------------------------------------------------------------------
-- org."group"
-- ---------------------------------------------------------------------------

CREATE TABLE org."group" (
  id                      uuid        NOT NULL PRIMARY KEY,
  party_id                uuid        NOT NULL UNIQUE REFERENCES org.party(id),
  code                    text        NOT NULL UNIQUE CHECK (code ~ '^[A-Z0-9_\-\.]{1,40}$'),
  consolidation_method    text        NOT NULL DEFAULT 'FULL'
                          CHECK (consolidation_method IN ('FULL','PROPORTIONAL','EQUITY')),
  reporting_currency_code char(3)     NOT NULL REFERENCES org.currency(code),
  created_at              timestamptz NOT NULL,
  created_by              uuid        NOT NULL,
  updated_at              timestamptz NOT NULL,
  updated_by              uuid        NOT NULL
);
-- party_id already has a leading index via its UNIQUE constraint; this one
-- is added on top because reporting_currency_code has no index yet (DBC-3).
CREATE INDEX ix_group_reporting_currency ON org."group"(reporting_currency_code);

-- ---------------------------------------------------------------------------
-- org.legal_entity
-- ---------------------------------------------------------------------------

CREATE TABLE org.legal_entity (
  id            uuid        PRIMARY KEY,
  party_id      uuid        NOT NULL UNIQUE REFERENCES org.party(id),
  group_id      uuid        NOT NULL REFERENCES org."group"(id),
  code          text        NOT NULL UNIQUE CHECK (code ~ '^[A-Z0-9_\-\.]{1,40}$'),
  currency_code char(3)     NOT NULL REFERENCES org.currency(code),
  country_code  char(2)     NOT NULL,
  is_active     boolean     NOT NULL DEFAULT true,
  created_at    timestamptz NOT NULL,
  created_by    uuid        NOT NULL,
  updated_at    timestamptz NOT NULL,
  updated_by    uuid        NOT NULL
);
CREATE INDEX ix_legal_entity_group ON org.legal_entity(group_id);
-- Added (currency_code has no index of its own in the domain DDL, DBC-3).
CREATE INDEX ix_legal_entity_currency ON org.legal_entity(currency_code);

-- ---------------------------------------------------------------------------
-- org.business_unit
-- ---------------------------------------------------------------------------

CREATE TABLE org.business_unit (
  id               uuid        PRIMARY KEY,
  party_id         uuid        NOT NULL UNIQUE REFERENCES org.party(id),
  legal_entity_id  uuid        NOT NULL REFERENCES org.legal_entity(id),
  code             text        NOT NULL CHECK (code ~ '^[A-Z0-9_\-\.]{1,40}$'),
  caption_i18n     jsonb       NOT NULL CHECK (jsonb_typeof(caption_i18n) = 'object' AND caption_i18n ? 'de'),
  cost_center_code text        NULL,
  is_active        boolean     NOT NULL DEFAULT true,
  created_at       timestamptz NOT NULL,
  created_by       uuid        NOT NULL,
  updated_at       timestamptz NOT NULL,
  updated_by       uuid        NOT NULL,
  UNIQUE (legal_entity_id, code)
);
CREATE INDEX ix_business_unit_le ON org.business_unit(legal_entity_id);

-- ---------------------------------------------------------------------------
-- org.location
--
-- NOTE: no latitude/longitude here on purpose. Those two columns arrive in
-- step 1.1, not 0.4.
-- ---------------------------------------------------------------------------

CREATE TABLE org.location (
  id           uuid        PRIMARY KEY,
  code         text        NULL UNIQUE CHECK (code ~ '^[A-Z0-9_\-\.]{1,40}$'),
  name         text        NOT NULL,
  street       text        NULL,
  postal_code  text        NULL,
  city         text        NULL,
  region       text        NULL,
  country_code char(2)     NOT NULL,
  is_active    boolean     NOT NULL DEFAULT true,
  created_at   timestamptz NOT NULL,
  created_by   uuid        NOT NULL,
  updated_at   timestamptz NOT NULL,
  updated_by   uuid        NOT NULL
);

-- ---------------------------------------------------------------------------
-- org.warehouse
-- ---------------------------------------------------------------------------

CREATE TABLE org.warehouse (
  id               uuid        PRIMARY KEY,
  location_id      uuid        NOT NULL REFERENCES org.location(id),
  legal_entity_id  uuid        NOT NULL REFERENCES org.legal_entity(id),
  business_unit_id uuid        NULL REFERENCES org.business_unit(id),
  code             text        NOT NULL UNIQUE CHECK (code ~ '^[A-Z0-9_\-\.]{1,40}$'),
  caption_i18n     jsonb       NOT NULL CHECK (jsonb_typeof(caption_i18n) = 'object' AND caption_i18n ? 'de'),
  is_active        boolean     NOT NULL DEFAULT true,
  created_at       timestamptz NOT NULL,
  created_by       uuid        NOT NULL,
  updated_at       timestamptz NOT NULL,
  updated_by       uuid        NOT NULL
);
CREATE INDEX ix_warehouse_le ON org.warehouse(legal_entity_id);
CREATE INDEX ix_warehouse_bu ON org.warehouse(business_unit_id);
-- Added (location_id has no index of its own in the domain DDL, DBC-3).
CREATE INDEX ix_warehouse_location ON org.warehouse(location_id);

-- ---------------------------------------------------------------------------
-- org.app_user
-- ---------------------------------------------------------------------------

CREATE TABLE org.app_user (
  id                 uuid        PRIMARY KEY,
  party_id           uuid        NOT NULL UNIQUE REFERENCES org.party(id),
  business_unit_id   uuid        NULL REFERENCES org.business_unit(id),
  external_subject   text        NULL UNIQUE,
  email              text        NULL,
  preferred_language char(2)     NOT NULL DEFAULT 'de' CHECK (preferred_language IN ('de','en','fr','it','es')),
  is_active          boolean     NOT NULL DEFAULT true,
  created_at         timestamptz NOT NULL,
  created_by         uuid        NOT NULL,
  updated_at         timestamptz NOT NULL,
  updated_by         uuid        NOT NULL
);
-- Added (business_unit_id has no index of its own in the domain DDL, DBC-3).
CREATE INDEX ix_app_user_bu ON org.app_user(business_unit_id);

-- ORG-R9: email is unique among active users (conventions-database.md §9:
-- "unicidades de negocio como UNIQUE, o índice único parcial si hay
-- is_active" -- a plain UNIQUE would wrongly block reusing a deactivated
-- user's email for someone new). lower() so the uniqueness is
-- case-insensitive, matching how email addresses are actually compared.
CREATE UNIQUE INDEX uq_app_user_email_active ON org.app_user (lower(email))
  WHERE is_active AND email IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Privileges (exact; no more, no less)
--
-- svc_platform: DML (INSERT, UPDATE, SELECT) on every table. No DELETE:
-- master data has no DELETE (conventions-api.md §1).
--
-- All other business service roles: SELECT only. org is a reference schema
-- (architecture.md §3): any service may read it.
--
-- svc_solver: SELECT, following the exact pattern of domain/access.md §5,
-- applied here for the first time because org did not exist before this
-- migration. The ALTER DEFAULT PRIVILEGES makes any table org gains in a
-- later step (e.g. org.exchange_rate in 1.1) automatically readable by
-- svc_solver too, without a follow-up grant.
--
-- svc_relay gets nothing on org: it never touches business schemas, only
-- outbox/inbox.
-- ---------------------------------------------------------------------------

REVOKE ALL ON SCHEMA org FROM PUBLIC;

GRANT USAGE ON SCHEMA org TO
  svc_platform, svc_catalog, svc_operations, svc_commercial,
  svc_resources, svc_controlling, svc_field, svc_solver;

GRANT INSERT, UPDATE, SELECT ON
  org.currency,
  org.party,
  org."group",
  org.legal_entity,
  org.business_unit,
  org.location,
  org.warehouse,
  org.app_user
TO svc_platform;

GRANT SELECT ON
  org.currency,
  org.party,
  org."group",
  org.legal_entity,
  org.business_unit,
  org.location,
  org.warehouse,
  org.app_user
TO svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field, svc_solver;

ALTER DEFAULT PRIVILEGES FOR ROLE nexus_owner IN SCHEMA org
  GRANT SELECT ON TABLES TO svc_solver;
