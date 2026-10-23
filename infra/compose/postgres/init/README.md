`00_run_bootstrap.sql` is the only file `docker-entrypoint-initdb.d` sees here: a
one-line `\i /bootstrap/00_roles.sql` that runs `db/bootstrap/00_roles.sql`
(mounted read-only at `/bootstrap` in `docker-compose.yml`), which creates
`nexus_owner`, `nexus_migrator` and every `svc_<service>` role. Flyway migrations
then create schemas, tables and grants (paso 0.3).

It is a `.sql` `\i` indirection rather than a `.sh` wrapper on purpose: Docker
Desktop's bind mount of an individually-mounted host file does not reliably
preserve the executable bit, which breaks the shebang-exec path
docker-entrypoint-initdb.d uses for `.sh` scripts. `\i` only needs read access.
