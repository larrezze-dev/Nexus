-- Common seed: org currencies and the SYSTEM user (step 0.4).
--
-- Applied by Flyway (nexus_migrator) after V202610241000__org__core.sql.
-- Idempotent: every INSERT uses ON CONFLICT DO NOTHING (conventions-database.md §10),
-- so re-applying it does not duplicate rows or fail.
--
-- SYSTEM (id 00000000-0000-7000-8000-000000000001) is the only actor that
-- jobs and auto-provisioning use (conventions-database.md §4). It has no
-- external_subject (never logs in) and no business_unit_id (belongs to no BU).
-- It audits its own creation: created_by/updated_by point to its own id.

-- ---------------------------------------------------------------------------
-- 1. Currencies (org.currency)
-- ---------------------------------------------------------------------------
INSERT INTO org.currency (code, caption_i18n, minor_units, is_active, created_at, created_by, updated_at, updated_by)
VALUES
  ('CHF', '{"de":"Schweizer Franken","en":"Swiss Franc","fr":"Franc suisse","it":"Franco svizzero","es":"Franco suizo"}', 2, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('EUR', '{"de":"Euro","en":"Euro","fr":"Euro","it":"Euro","es":"Euro"}', 2, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('USD', '{"de":"US-Dollar","en":"US Dollar","fr":"Dollar américain","it":"Dollaro statunitense","es":"Dólar estadounidense"}', 2, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('GBP', '{"de":"Britisches Pfund","en":"British Pound","fr":"Livre sterling","it":"Sterlina britannica","es":"Libra esterlina"}', 2, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;

-- ---------------------------------------------------------------------------
-- 2. SYSTEM party (org.party) and SYSTEM user (org.app_user)
-- ---------------------------------------------------------------------------
INSERT INTO org.party (id, party_type, legal_name, display_name, is_internal, is_active, created_at, created_by, updated_at, updated_by)
VALUES ('00000000-0000-7000-8000-000000000001', 'PERSON', 'SYSTEM', 'System', true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (id) DO NOTHING;

INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by)
VALUES ('00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-000000000001', NULL, NULL, NULL, 'en', true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (id) DO NOTHING;
