-- Seeds the permission catalog entries introduced by step 0.4 (org core).
-- NOT a live Flyway migration yet: access.permission does not exist until
-- step 0.6. This file is picked up (renamed with a real V<ts>__ prefix) by
-- step 0.6's track C once that table exists. Flyway ignores this filename
-- today because it does not match its V.../R... naming pattern.
--
-- Columns match the real DDL (domain/access.md §3): id (uuid PK, no
-- DEFAULT), code, resource_type (not "resource"), action, caption_i18n
-- (NOT NULL, "de" key required per conventions-database.md §5).

INSERT INTO access.permission (id, code, resource_type, action, caption_i18n, created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000c0001', 'PARTY:READ', 'PARTY', 'READ', '{"de":"Parties ansehen","en":"View parties"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0002', 'PARTY:CREATE', 'PARTY', 'CREATE', '{"de":"Party anlegen","en":"Create party"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0003', 'PARTY:UPDATE', 'PARTY', 'UPDATE', '{"de":"Party bearbeiten","en":"Update party"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0004', 'ORG_STRUCTURE:READ', 'ORG_STRUCTURE', 'READ', '{"de":"Organisationsstruktur ansehen","en":"View org structure"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0005', 'ORG_STRUCTURE:CREATE', 'ORG_STRUCTURE', 'CREATE', '{"de":"Organisationsstruktur anlegen","en":"Create org structure"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0006', 'ORG_STRUCTURE:UPDATE', 'ORG_STRUCTURE', 'UPDATE', '{"de":"Organisationsstruktur bearbeiten","en":"Update org structure"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0007', 'USER:READ', 'USER', 'READ', '{"de":"Benutzer ansehen","en":"View users"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0008', 'USER:CREATE', 'USER', 'CREATE', '{"de":"Benutzer anlegen","en":"Create user"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0009', 'USER:UPDATE', 'USER', 'UPDATE', '{"de":"Benutzer bearbeiten","en":"Update user"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c000a', 'CURRENCY:READ', 'CURRENCY', 'READ', '{"de":"Währungen ansehen","en":"View currencies"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c000b', 'CURRENCY:UPDATE', 'CURRENCY', 'UPDATE', '{"de":"Währungen bearbeiten","en":"Update currencies"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;
