-- The 11 seeded roles (domain/access.md §7) plus the SEED GLOBAL
-- role_assignment for SYSTEM (conventions-database.md §7: "el usuario
-- SYSTEM tiene un RoleAssignment GLOBAL sembrado en 0.6"). All 11 are
-- is_system = true: they are structural roles other code depends on
-- (ACC-R9: a role with is_system = true cannot be deactivated via the API).

INSERT INTO access.role (id, code, caption_i18n, description, is_system, is_active, created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000d0001', 'SYSTEM_ADMIN',       '{"de":"Systemadministrator","en":"System administrator"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0002', 'ORG_ADMIN',          '{"de":"Organisationsadministrator","en":"Organization administrator"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0003', 'SECURITY_ADMIN',     '{"de":"Sicherheitsadministrator","en":"Security administrator"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0004', 'CATALOG_ADMIN',      '{"de":"Katalogadministrator","en":"Catalog administrator"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0005', 'WAREHOUSE_OPERATOR', '{"de":"Lagermitarbeiter","en":"Warehouse operator"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0006', 'PROJECT_MANAGER',    '{"de":"Projektleiter","en":"Project manager"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0007', 'SALES',              '{"de":"Vertrieb","en":"Sales"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0008', 'CONTROLLER',         '{"de":"Controller","en":"Controller"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d0009', 'HR_ADMIN',           '{"de":"Personaladministrator","en":"HR administrator"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d000a', 'FIELD_CAPTURE',      '{"de":"Felderfassung","en":"Field capture"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000d000b', 'VIEWER',             '{"de":"Betrachter","en":"Viewer"}', NULL, true, true, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;

INSERT INTO access.role_assignment (id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by)
VALUES (
  '00000000-0000-7000-8000-0000000e0001',
  '00000000-0000-7000-8000-000000000001',
  '00000000-0000-7000-8000-0000000d0001',
  'GLOBAL', NULL, now(), NULL,
  '00000000-0000-7000-8000-000000000001',
  'SEED', NULL, NULL,
  now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'
)
ON CONFLICT (id) DO NOTHING;
