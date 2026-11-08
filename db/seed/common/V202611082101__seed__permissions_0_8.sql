-- Seeds the permission catalog entry introduced by step 0.8 (audit trail over
-- nexus_analytics.event_log) and its default role grants. Mirrors
-- contracts/permissions.yaml's defaultRoles for AUDIT:READ exactly (every
-- initial role except FIELD_CAPTURE).
--
-- Same conventions as V202610300902 / V202610300903: fixed deterministic ids,
-- idempotent via ON CONFLICT, system user as author.
-- Depends on access.role (V202610300900) -- guaranteed by this file's later version.

INSERT INTO access.permission (id, code, resource_type, action, caption_i18n, created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000c0012', 'AUDIT:READ', 'AUDIT', 'READ', '{"de":"Änderungsverlauf ansehen","en":"View audit trail"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;

INSERT INTO access.role_permission (id, role_id, permission_id, valid_from, valid_to, granted_by)
SELECT m.id::uuid, r.id, p.id, now(), NULL, '00000000-0000-7000-8000-000000000001'
FROM (VALUES
  ('00000000-0000-7000-8000-0000000f0039', 'SYSTEM_ADMIN',       'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f003a', 'ORG_ADMIN',          'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f003b', 'SECURITY_ADMIN',     'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f003c', 'CATALOG_ADMIN',      'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f003d', 'WAREHOUSE_OPERATOR', 'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f003e', 'PROJECT_MANAGER',    'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f003f', 'SALES',              'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f0040', 'CONTROLLER',         'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f0041', 'HR_ADMIN',           'AUDIT:READ'),
  ('00000000-0000-7000-8000-0000000f0042', 'VIEWER',             'AUDIT:READ')
) AS m(id, role_code, permission_code)
JOIN access.role r ON r.code = m.role_code
JOIN access.permission p ON p.code = m.permission_code
ON CONFLICT (id) DO NOTHING;
