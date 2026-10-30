-- Seeds the permission catalog entries introduced by step 0.6 (access/RBAC).
-- Columns match access.permission (domain/access.md §3).

INSERT INTO access.permission (id, code, resource_type, action, caption_i18n, created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000c000c', 'ROLE:READ',             'ROLE',             'READ',   '{"de":"Rollen ansehen","en":"View roles"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c000d', 'ROLE:CREATE',           'ROLE',             'CREATE', '{"de":"Rolle anlegen","en":"Create role"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c000e', 'ROLE:UPDATE',           'ROLE',             'UPDATE', '{"de":"Rolle bearbeiten","en":"Update role"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c000f', 'ROLE_ASSIGNMENT:READ',   'ROLE_ASSIGNMENT', 'READ',   '{"de":"Rollenzuweisungen ansehen","en":"View role assignments"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0010', 'ROLE_ASSIGNMENT:CREATE', 'ROLE_ASSIGNMENT', 'CREATE', '{"de":"Rollenzuweisung anlegen","en":"Create role assignment"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'),
  ('00000000-0000-7000-8000-0000000c0011', 'ROLE_ASSIGNMENT:UPDATE', 'ROLE_ASSIGNMENT', 'UPDATE', '{"de":"Rollenzuweisung bearbeiten/widerrufen","en":"Update/revoke role assignment"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;
