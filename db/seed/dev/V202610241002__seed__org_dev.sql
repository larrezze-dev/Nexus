-- Dev seed: organisational structure and sample users (conventions-database.md §10, domain/org.md §3 "Semilla dev").
--
-- Creates the NUSSLI group, two legal entities (LE-CH in CHF/CH, LE-DE in EUR/DE),
-- one business unit and one warehouse per legal entity, two locations, and the
-- app_user rows that match the Keycloak users seeded in step 0.2
-- (infra/compose/keycloak/realm-nexus.json). Every party here is is_internal = true.
--
-- Requires db/seed/common (currency CHF/EUR and the SYSTEM user) to be applied first.
-- All ids are fixed UUIDs in the range 00000000-0000-7000-8000-0000000b0xxx, reserved
-- for this seed. created_by / updated_by = SYSTEM (00000000-0000-7000-8000-000000000001).
--
-- Idempotent: every INSERT is ON CONFLICT DO NOTHING. Tables whose natural key is a
-- UNIQUE column (group.code, legal_entity.code, warehouse.code, app_user.external_subject)
-- use that column as the conflict target; the rest use the primary key.
--
-- The user 'unprovisioned' (oid 00000000-0000-7000-8000-00000000a0ff) is deliberately
-- NOT created here: it must have neither a Party nor an app_user, so that the
-- user_not_provisioned (403) path can be tested.

-- ---------------------------------------------------------------------------
-- Parties (directory). Groups, LEs and BUs are ORGANIZATION parties; users are PERSON.

INSERT INTO org.party (id, party_type, legal_name, display_name, is_internal, is_active,
                       created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000b0101', 'ORGANIZATION', 'Nüssli Group',                  'Nüssli',        true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0102', 'ORGANIZATION', 'Nüssli Event & Rental AG',     'Nüssli CH',     true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0103', 'ORGANIZATION', 'Nüssli Event & Rental GmbH',   'Nüssli DE',     true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0104', 'ORGANIZATION', 'Nüssli Events CH',             'Events CH',     true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0105', 'ORGANIZATION', 'Nüssli Events DE',             'Events DE',     true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0111', 'PERSON',       'Admin User',                    'Admin',         true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0112', 'PERSON',       'User A',                        'User A',        true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0113', 'PERSON',       'User B',                        'User B',        true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0114', 'PERSON',       'Warehouse Agent A',             'Warehouse A',   true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (id) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Group NUSSLI

INSERT INTO org."group" (id, party_id, code, consolidation_method, reporting_currency_code,
                         created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000b0201', '00000000-0000-7000-8000-0000000b0101', 'NUSSLI', 'FULL', 'CHF',
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Legal entities

INSERT INTO org.legal_entity (id, party_id, group_id, code, currency_code, country_code, is_active,
                              created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000b0301', '00000000-0000-7000-8000-0000000b0102', '00000000-0000-7000-8000-0000000b0201',
   'LE-CH', 'CHF', 'CH', true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0302', '00000000-0000-7000-8000-0000000b0103', '00000000-0000-7000-8000-0000000b0201',
   'LE-DE', 'EUR', 'DE', true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Business units (one per legal entity)

INSERT INTO org.business_unit (id, party_id, legal_entity_id, code, caption_i18n, is_active,
                               created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000b0401', '00000000-0000-7000-8000-0000000b0104', '00000000-0000-7000-8000-0000000b0301',
   'BU-CH-EVENTS', '{"de":"Events CH","en":"Events CH"}'::jsonb, true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0402', '00000000-0000-7000-8000-0000000b0105', '00000000-0000-7000-8000-0000000b0302',
   'BU-DE-EVENTS', '{"de":"Events DE","en":"Events DE"}'::jsonb, true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (id) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Locations (geo coordinates arrive in step 1.1)

INSERT INTO org.location (id, code, name, street, postal_code, city, country_code, is_active,
                          created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000b0501', 'LOC-CH-01', 'Lager Zug', NULL, '6300', 'Zug', 'CH', true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0502', 'LOC-DE-01', 'Lager München', NULL, '80331', 'München', 'DE', true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (id) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Warehouses (one per legal entity, each tied to its BU)

INSERT INTO org.warehouse (id, location_id, legal_entity_id, business_unit_id, code, caption_i18n, is_active,
                           created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000b0601', '00000000-0000-7000-8000-0000000b0501', '00000000-0000-7000-8000-0000000b0301',
   '00000000-0000-7000-8000-0000000b0401', 'WH-CH-01', '{"de":"Lager CH 01","en":"Warehouse CH 01"}'::jsonb, true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0602', '00000000-0000-7000-8000-0000000b0502', '00000000-0000-7000-8000-0000000b0302',
   '00000000-0000-7000-8000-0000000b0402', 'WH-DE-01', '{"de":"Lager DE 01","en":"Warehouse DE 01"}'::jsonb, true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Application users (external_subject = Keycloak oid from realm-nexus.json, step 0.2)
-- Scope (GLOBAL/LE/warehouse) is not stored here; it is granted by step 0.6.

INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active,
                          created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000b0711', '00000000-0000-7000-8000-0000000b0111', '00000000-0000-7000-8000-0000000b0401',
   '00000000-0000-7000-8000-00000000a001', 'admin@nexus.dev.local', 'de', true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0712', '00000000-0000-7000-8000-0000000b0112', '00000000-0000-7000-8000-0000000b0401',
   '00000000-0000-7000-8000-00000000a002', 'usera@nexus.dev.local', 'de', true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0713', '00000000-0000-7000-8000-0000000b0113', '00000000-0000-7000-8000-0000000b0402',
   '00000000-0000-7000-8000-00000000a003', 'userb@nexus.dev.local', 'en', true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000b0714', '00000000-0000-7000-8000-0000000b0114', '00000000-0000-7000-8000-0000000b0401',
   '00000000-0000-7000-8000-00000000a004', 'wha@nexus.dev.local', 'de', true,
   now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (external_subject) DO NOTHING;
