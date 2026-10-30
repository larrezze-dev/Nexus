-- access core: Permission, Role, RolePermission (A10 temporal), RoleAssignment
-- (docs/domain/access.md §3; step 0.6, track C).
--
-- access is the security schema (architecture.md §3): only svc_platform has
-- DML. No other service role gets SELECT on these tables -- every other
-- service reads scope/permission state exclusively through the
-- SECURITY DEFINER functions in R__access__scope_functions.sql, never by
-- querying these tables directly.
--
-- No RLS in this schema (domain/access.md §4 preamble, conventions-database.md
-- §7: "Esquema access: sus tablas no tienen RLS").
--
-- Ownership (DBC-6): db/migrations/afterConnect.sql already runs
-- `SET ROLE nexus_owner` for the whole Flyway session, so every object
-- created below is owned by nexus_owner without any explicit ALTER ... OWNER.
--
-- Standard columns (conventions-database.md §4, "-- + std" in the domain
-- doc): created_at/created_by/updated_at/updated_by are set by the
-- application via TimeProvider, never by a column DEFAULT.
-- access.permission, access.role and access.role_assignment carry this
-- block; access.role_permission does not (it is its own temporal shape:
-- id/valid_from/valid_to/granted_by).

CREATE SCHEMA access;

-- ---------------------------------------------------------------------------
-- access.permission
-- ---------------------------------------------------------------------------

CREATE TABLE access.permission (
  id            uuid        PRIMARY KEY,
  code          text        NOT NULL UNIQUE,
  resource_type text        NOT NULL,
  action        text        NOT NULL CHECK (action IN ('CREATE','READ','UPDATE','DELETE','APPROVE','EXPORT')),
  caption_i18n  jsonb       NOT NULL CHECK (jsonb_typeof(caption_i18n) = 'object' AND caption_i18n ? 'de'),
  created_at    timestamptz NOT NULL,
  created_by    uuid        NOT NULL,
  updated_at    timestamptz NOT NULL,
  updated_by    uuid        NOT NULL,
  CHECK (code = resource_type || ':' || action)
);

-- ---------------------------------------------------------------------------
-- access.role
-- ---------------------------------------------------------------------------

CREATE TABLE access.role (
  id           uuid        PRIMARY KEY,
  code         text        NOT NULL UNIQUE,
  caption_i18n jsonb       NOT NULL CHECK (jsonb_typeof(caption_i18n) = 'object' AND caption_i18n ? 'de'),
  description  text        NULL,
  is_system    boolean     NOT NULL DEFAULT false,
  is_active    boolean     NOT NULL DEFAULT true,
  created_at   timestamptz NOT NULL,
  created_by   uuid        NOT NULL,
  updated_at   timestamptz NOT NULL,
  updated_by   uuid        NOT NULL
);

-- ---------------------------------------------------------------------------
-- access.role_permission (A10: which permissions a role had, and when)
-- ---------------------------------------------------------------------------

CREATE TABLE access.role_permission (
  id            uuid        PRIMARY KEY,
  role_id       uuid        NOT NULL REFERENCES access.role(id),
  permission_id uuid        NOT NULL REFERENCES access.permission(id),
  valid_from    timestamptz NOT NULL,
  valid_to      timestamptz NULL,
  granted_by    uuid        NOT NULL,
  EXCLUDE USING gist (role_id WITH =, permission_id WITH =,
                      tstzrange(valid_from, coalesce(valid_to,'infinity')) WITH &&)
);
CREATE INDEX ix_role_permission_role ON access.role_permission(role_id);
CREATE INDEX ix_role_permission_permission ON access.role_permission(permission_id);

-- ---------------------------------------------------------------------------
-- access.role_assignment
-- ---------------------------------------------------------------------------

CREATE TABLE access.role_assignment (
  id              uuid        PRIMARY KEY,
  user_id         uuid        NOT NULL REFERENCES org.app_user(id),
  role_id         uuid        NOT NULL REFERENCES access.role(id),
  scope_type      text        NOT NULL CHECK (scope_type IN
                    ('GROUP','LEGAL_ENTITY','BUSINESS_UNIT','WAREHOUSE','PROJECT','GLOBAL')),
  scope_entity_id uuid        NULL,
  valid_from      timestamptz NOT NULL,
  valid_to        timestamptz NULL,
  granted_by      uuid        NOT NULL,
  source          text        NOT NULL DEFAULT 'MANUAL'
                  CHECK (source IN ('MANUAL','RESOURCE_ASSIGNMENT','SEED')),
  source_ref_id   uuid        NULL,
  revoked_by      uuid        NULL,
  created_at      timestamptz NOT NULL,
  created_by      uuid        NOT NULL,
  updated_at      timestamptz NOT NULL,
  updated_by      uuid        NOT NULL,
  CHECK ((scope_type = 'GLOBAL') = (scope_entity_id IS NULL)),
  -- Deviation from the literal domain/access.md §3 text, which has
  -- `CHECK (valid_to IS NULL OR valid_to > valid_from)`: that blocks the
  -- normal case of revoking/cancelling an assignment whose valid_from is
  -- still in the future (ACC-R3's "revocation sets valid_to = now()" and
  -- ACC-R8's "cancelling a future FIELD_CAPTURE grant" both produce
  -- valid_to = now() < valid_from). Relaxed to only forbid backdating
  -- valid_to before the row itself existed; ordering between valid_from and
  -- valid_to is an application concern (conventions-database.md §9: cross-
  -- field business rules belong in the domain, not in a CHECK), decided by
  -- Luciano on review.
  CHECK (valid_to IS NULL OR valid_to >= created_at),
  CHECK ((source = 'RESOURCE_ASSIGNMENT') = (source_ref_id IS NOT NULL))
);
CREATE INDEX ix_ra_user_active ON access.role_assignment (user_id, valid_from, valid_to);
CREATE INDEX ix_ra_source_ref  ON access.role_assignment (source_ref_id) WHERE source_ref_id IS NOT NULL;
-- Added: role_id is a FK with no index of its own in the domain DDL (DBC-3).
-- scope_entity_id is polymorphic, not a FK (no DBC-3 obligation), but it is
-- looked up by every access.visible_*() function, so it still gets an index.
CREATE INDEX ix_ra_role ON access.role_assignment(role_id);
CREATE INDEX ix_ra_scope_entity ON access.role_assignment(scope_entity_id) WHERE scope_entity_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Privileges (exact; no more, no less)
--
-- access is the security schema: only svc_platform gets DML (INSERT,
-- UPDATE, SELECT -- no DELETE, master data convention: corrections are new
-- rows, not deletes). No other service role gets anything here; they reach
-- this data only through the SECURITY DEFINER functions granted in
-- R__access__scope_functions.sql, which run with nexus_owner's own
-- privileges regardless of the caller's grants on these tables.
-- ---------------------------------------------------------------------------

REVOKE ALL ON SCHEMA access FROM PUBLIC;

GRANT USAGE ON SCHEMA access TO svc_platform;

GRANT INSERT, UPDATE, SELECT ON
  access.permission,
  access.role,
  access.role_permission,
  access.role_assignment
TO svc_platform;
