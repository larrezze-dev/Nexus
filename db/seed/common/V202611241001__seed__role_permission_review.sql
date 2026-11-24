-- Reviewed default grants (decided by Luciano, 2026-11-21): two defaultRoles of
-- contracts/permissions.yaml were added after the defaults matrix was first seeded
-- (V202610300903__seed__role_permission_defaults.sql), so this file adds only the
-- two new rows. The earlier seed is versioned and merged: it is not edited.
--
--   ROLE:READ           + ORG_ADMIN       (ids ...0f0044)
--     ORG_ADMIN needs to read roles to use the role-assignment form.
--   ORG_STRUCTURE:READ  + SECURITY_ADMIN  (ids ...0f0045)
--     SECURITY_ADMIN needs to read the org structure to use the role-assignment form.
--
-- Same conventions as V202610300903 and V202611141001: fixed deterministic ids,
-- idempotent via ON CONFLICT (id), system user as granter. Also skips a pair that
-- already has an open grant under another id (e.g. granted by hand through the API):
-- the EXCLUDE constraint on role_permission would reject a second open window.
-- Depends on access.role, access.permission and access.role_permission of earlier seeds.

INSERT INTO access.role_permission (id, role_id, permission_id, valid_from, valid_to, granted_by)
SELECT m.id::uuid, r.id, p.id, now(), NULL, '00000000-0000-7000-8000-000000000001'
FROM (VALUES
  ('00000000-0000-7000-8000-0000000f0044', 'ORG_ADMIN',       'ROLE:READ'),
  ('00000000-0000-7000-8000-0000000f0045', 'SECURITY_ADMIN',  'ORG_STRUCTURE:READ')
) AS m(id, role_code, permission_code)
JOIN access.role r ON r.code = m.role_code
JOIN access.permission p ON p.code = m.permission_code
WHERE NOT EXISTS (
  SELECT 1 FROM access.role_permission x
  WHERE x.role_id = r.id AND x.permission_id = p.id AND (x.valid_to IS NULL OR x.valid_to > now())
)
ON CONFLICT (id) DO NOTHING;
