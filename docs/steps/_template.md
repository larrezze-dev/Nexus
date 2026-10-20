# Paso X.Y · Título

| | |
|---|---|
| **Fase** | X · nombre |
| **Tracks** | C → (B ∥ S ∥ F) → Gate D |
| **Depende de** | pasos en `Aprobado` |
| **Bloqueado por** | ADR o ⚑ que deben estar cerrados |
| **Paralelo con** | pasos que pueden correr a la vez en otra sesión |
| **Servicio / esquema** | `nexus-…` / `…` |

## Objetivo

Dos o tres frases: qué queda funcionando al terminar y por qué importa.

## Leer

Solo esto, según tu track. Nada más de `docs/` salvo que una regla citada lo exija.

| Track | Leer |
|---|---|
| C | |
| B | |
| S | |
| F | |

## Track C · Contrato

Rama `step/X.Y-contract`. Termina en **Gate C**.

**Entregables**
- `db/migrations/V<ts>__<ctx>__<desc>.sql`: …
- `db/migrations/R__<ctx>__rls.sql`: …
- `contracts/openapi/<servicio>.yaml`: operaciones …
- `contracts/events/<ctx>/…avsc`: …
- `contracts/permissions.yaml` + `db/seed/common/…`: permisos …

**Aceptación C**
- [ ] `make contracts` y job `db` de CI en verde (DBC-1…6).
- [ ] …

## Track B · Backend

Rama `step/X.Y-backend`, desde `main` con el contrato fusionado.

**Entregables** · **Reglas con test** · **Aceptación B**

## Track S · Servicios

Rama `step/X.Y-services`. Consumidores, proyecciones y jobs.

## Track F · Frontend

Rama `step/X.Y-web`. Contra el mock (`make mock-api S=<servicio>`, `ng serve --configuration mock`).

## Integración (Gate D)

Escenarios e2e en `tests/e2e/X.Y-*.spec.ts` contra el entorno completo.

## Fuera de alcance

## Verificación de Luciano

Pasos concretos, con el resultado esperado de cada uno.
