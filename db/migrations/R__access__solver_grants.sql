-- svc_solver read grants (domain/access.md §5). Repeatable migration: each
-- step that creates one of these schemas (0.4 org, 1.2 uom, 1.3 catalog,
-- 2.1 project, 3.1 stock, 4.1 logistics) appends its own block here. Only
-- org exists as of 0.6; the grants below duplicate what
-- V202610241000__org__core.sql already granted svc_solver directly (GRANT
-- is idempotent, so re-applying is a no-op) -- this file is the canonical,
-- growing record the later steps extend, per the comment in
-- domain/access.md §5.

GRANT USAGE ON SCHEMA org TO svc_solver;
GRANT SELECT ON ALL TABLES IN SCHEMA org TO svc_solver;
ALTER DEFAULT PRIVILEGES FOR ROLE nexus_owner IN SCHEMA org
  GRANT SELECT ON TABLES TO svc_solver;
