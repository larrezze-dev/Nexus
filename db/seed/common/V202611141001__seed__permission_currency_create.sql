-- Seeds CURRENCY:CREATE (POST /v1/currencies, UI contract 2) and its default grant.
-- Mirrors contracts/permissions.yaml (defaultRoles: SYSTEM_ADMIN only).
-- Same conventions as V202610300901 / V202611082101: fixed deterministic ids,
-- idempotent via ON CONFLICT, system user as author.
-- Depends on access.role (V202610300900) and access.permission rows of earlier seeds.

INSERT INTO access.permission (id, code, resource_type, action, caption_i18n, created_at, created_by, updated_at, updated_by)
VALUES
  ('00000000-0000-7000-8000-0000000c0013', 'CURRENCY:CREATE', 'CURRENCY', 'CREATE', '{"de":"Währungen anlegen","en":"Create currencies"}', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001')
ON CONFLICT (code) DO NOTHING;

INSERT INTO access.role_permission (id, role_id, permission_id, valid_from, valid_to, granted_by)
SELECT m.id::uuid, r.id, p.id, now(), NULL, '00000000-0000-7000-8000-000000000001'
FROM (VALUES
  ('00000000-0000-7000-8000-0000000f0043', 'SYSTEM_ADMIN', 'CURRENCY:CREATE')
) AS m(id, role_code, permission_code)
JOIN access.role r ON r.code = m.role_code
JOIN access.permission p ON p.code = m.permission_code
ON CONFLICT (id) DO NOTHING;
