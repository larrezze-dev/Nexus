CREATE SCHEMA test;

CREATE TABLE test.append_only_probe (
  id         uuid        PRIMARY KEY,
  created_at timestamptz NOT NULL,
  created_by uuid        NOT NULL
);

REVOKE UPDATE, DELETE, TRUNCATE ON test.append_only_probe FROM PUBLIC, svc_platform, svc_catalog, svc_operations, svc_commercial, svc_resources, svc_controlling, svc_field;

CREATE TRIGGER trg_append_only_probe_append_only
  BEFORE UPDATE OR DELETE ON test.append_only_probe
  FOR EACH ROW EXECUTE FUNCTION infra.forbid_mutation();
