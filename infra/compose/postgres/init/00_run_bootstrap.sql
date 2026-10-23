-- The only file docker-entrypoint-initdb.d sees here. Deliberately NOT a .sh
-- wrapper: Docker Desktop's bind-mount of an individual host file can fail to
-- expose it as executable regardless of its host permission bits, which breaks
-- the shebang-exec path ("/bin/sh: bad interpreter: Permission denied"). `\i`
-- only needs psql to be able to read the target file, which the read-only bind
-- mount below already guarantees.
\i /bootstrap/00_roles.sql
