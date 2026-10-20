# NEXUS — Instrucciones para Claude Code

NEXUS es la plataforma greenfield que sustituye a EasyJob/adunic en Nüssli/NRP: material e infraestructura para eventos, multi-empresa y multi-almacén, alquiler interno, transferencias, compras, controlling y un Solver Timefold. No hay coexistencia con EasyJob: se migra una sola vez al final (ETL one-shot + big-bang).

## 1. Cómo trabajas aquí

1. **Una sesión = un paso + un track.** El usuario te dirá, por ejemplo, "Ejecuta 1.3 track B". La ficha está en `docs/steps/F<fase>/<id>-*.md`.
   Tracks: `C` contrato (DDL + OpenAPI + Avro) · `B` backend · `S` servicios (consumidores, proyecciones, jobs) · `F` frontend · `I` infra/tooling · `M` móvil · `J` Solver JVM.
2. **Lee solo:** este fichero, la ficha del paso y lo que su sección **"Leer"** indique para tu track. No explores el resto de `docs/` por iniciativa propia. Si la ficha cita una regla (`STK-R5`) y no la entiendes, lee solo la sección del dominio donde está.
3. **No leas los documentos `NEXUS_*.md` del Proyecto** (Manual, Parte 3, Parte 4). `docs/` los sustituye y resuelve sus contradicciones (`docs/00-foundation/source-reconciliation.md`).
4. **Plan antes de código.** Entra en plan mode y presenta: ficheros a crear o tocar, migraciones, endpoints, tests y las reglas `XXX-Rn` que cubre cada test. Espera aprobación explícita.
5. **Implementa solo los entregables de tu track.** "Fuera de alcance" no se toca, aunque sea trivial.
6. **Cierre del track:** tests en verde, la checklist "Aceptación" de tu track copiada y marcada en la descripción del PR (plantilla en `docs/00-foundation/delivery-process.md §3`), y la fila del paso en `docs/steps/STATUS.md` actualizada (`Gate C` tras el track C; `En revisión` cuando estén todos los tracks). **Nunca** pongas `Aprobado`: solo Luciano.

## 2. Cuándo PARAR y preguntar

- La ficha es ambigua, contradice un documento de dominio o falta una decisión de diseño.
- Tu track (B, S, F, M, J) necesita cambiar `contracts/` o `db/migrations/`: el contrato vuelve a Gate C. Solo el track C los modifica.
- Necesitas un paquete fuera de las listas aprobadas (`conventions-backend.md §10`, ADR-0010).
- El paso depende de un ADR que no está `Aceptado` (`docs/adr/README.md`) o de un ⚑ abierto que la ficha marca como bloqueante.
- Un test de un paso anterior se rompe y la causa no es obvia.

Para una decisión nueva, escribe un borrador de ADR en `docs/adr/` con estado `Propuesto` y para.

## 3. Reglas duras

- **Decisiones cerradas** (D1–D13, S1–S8, P1–P12 en `docs/00-foundation/principles.md §4`, y ADRs `Aceptado`): no se reabren ni se "mejoran" en silencio.
- **Esquemas y servicios:** un servicio solo escribe en sus esquemas (ARC-R1); FK entre esquemas solo hacia `org`, `uom`, `catalog` (ARC-R2); nunca lee el esquema transaccional de otro servicio (ARC-R3). Ver `architecture.md §3`.
- **Migraciones:** SQL plano con Flyway en `db/migrations/` (ADR-0001). Una versionada mergeada nunca se edita. EF Core nunca genera migraciones.
- **RLS:** toda tabla con `legal_entity_id` lleva `ENABLE` + `FORCE ROW LEVEL SECURITY` y su política (`conventions-database.md §7`), más los tres tests de aislamiento (`conventions-testing.md §3`).
- **Append-only:** trigger `infra.forbid_mutation()` y `REVOKE UPDATE, DELETE`. Se corrige con un asiento nuevo, nunca con `UPDATE`.
- **Versionado VR1:** `infra.guard_versioned_row()`; cambiar = cerrar la vigente e insertar otra (`domain/versioning.md`).
- **Outbox:** todo cambio que emite evento escribe `outbox.message` en la misma transacción, vía `UnitOfWork`. Nunca se produce a Kafka desde un handler.
- **Ledger:** solo `LedgerPostingService` escribe `stock.stock_movement` y `stock.stock_balance` (STK-R6). `qty` siempre en `stock_uom` (UR1).
- **Dinero y cantidades:** `numeric` en BD, string decimal en JSON, `decimal` en C#. Nunca `double`/`float`, nunca aritmética de dinero en el cliente.
- **Tiempo e ids:** `TimeProvider` inyectado; UUIDv7 generados en la aplicación (ADR-0009).
- **Marcas en los docs:** `[+]` = añadido por esta especificación; `⚑` = decisión abierta. Implementa los `[+]` tal cual y lístalos en el PR bajo "Elementos [+] implementados". No implementes nada que dependa de un `⚑` marcado como bloqueante.
- **Licencias:** prohibidos MediatR, AutoMapper, FluentAssertions y MassTransit (ADR-0007).
- **Secretos:** nunca en el repo. Local: `.env` a partir de `.env.example`.
- **Idioma:** código, identificadores, BD y commits en inglés. Docs y PR en español. UI con claves i18n en inglés y textos en `de`/`en`/`fr`/`it`/`es`.

## 4. Mapa

| Necesitas… | Fichero |
|---|---|
| Plan de pasos, dependencias, paralelismo y estado | `docs/steps/STATUS.md` |
| Gates, ramas, worktrees y plantilla de PR | `docs/00-foundation/delivery-process.md` |
| Servicios, esquemas, comunicación, puertos | `docs/00-foundation/architecture.md` |
| Estructura del repo, Flyway, CI | `docs/00-foundation/repo-structure.md` |
| Convenciones | `docs/00-foundation/conventions-*.md` |
| Modelo por contexto (DDL, reglas, estados, API, eventos, pantallas) | `docs/domain/<contexto>.md` |
| Decisiones de implementación | `docs/adr/` |
| Términos y equivalencias EasyJob | `docs/00-foundation/glossary.md` |

## 5. Comandos (se crean en 0.1–0.2)

```bash
make up | down | reset          # infraestructura local (compose)
make db-migrate                 # Flyway + migraciones ClickHouse
make seed-dev                   # semillas common + dev
make contracts                  # lint OpenAPI/Avro + cliente Angular + clases Avro
make mock-api S=<servicio>      # Prism sobre el contrato del servicio
make build | test | test-rls | test-e2e
make rule-coverage STEP=<id>    # reglas de la ficha sin test
```

## 6. Git

- Ramas: `step/<id>-contract`, `step/<id>-backend`, `step/<id>-services`, `step/<id>-web`, `step/<id>-infra`, `step/<id>-mobile`, `step/<id>-solver`. Cambios de especificación: `spec/<tema>`.
- Título de PR: `[<id>][<track>] <resumen>`, p. ej. `[1.3][B] Asset types backend`.
- Commits pequeños, en inglés, en imperativo.


## 7. Equipo de agentes y selección de modelo

La sesión principal **orquesta, no implementa**: descompone el track en subtareas, las delega en subagentes de `.claude/agents/` según la tabla y revisa sus resultados. Solo escribe código directamente si la subtarea es de nivel 3 y no se puede delegar.

| Nivel | Subagente (`model`) | Cuándo |
|---|---|---|
| 1 · Mecánico | `nx-mechanic` (`haiku`) | Localizar código o docs, regenerar clientes y clases (`make contracts`), semillas, claves i18n, ejecutar tests o lint y resumir el resultado, boilerplate que se deriva 1:1 del contrato (DTO, rutas, esquemas Avro de una tabla ya definida) |
| 2 · Implementación | `nx-implementer` (`sonnet`) | Handlers, endpoints, validadores, consumidores y réplicas `*_ref`, pantallas Angular con las plantillas de `conventions-frontend.md §4`, migraciones que copian una DDL ya escrita en `docs/domain/`, tests de reglas `XXX-Rn` |
| 3 · Crítico | `nx-architect` (`opus`) | RLS y funciones `access.*`; `LedgerPostingService` y todo lo que escribe `stock_movement`/`stock_balance`; `UnitOfWork`, Outbox y relay; versionado VR1; Solver (modelo y restricciones); consulta ATP; concurrencia y bloqueos; dinero, impuestos y conversión de unidades; migraciones que alteran tablas existentes; cualquier decisión no escrita en `docs/` |
| Revisión | `nx-reviewer` (`opus`) | Revisa todo lo de nivel 3 y una muestra de los niveles 1–2 antes de abrir el PR. Nunca revisa su propio trabajo |

Reglas:
1. **Plan con niveles:** el plan de cada track (plan mode) lista las subtareas con su nivel y el motivo en una línea. Luciano aprueba el plan y el reparto juntos.
2. **El nivel lo fija la subtarea, no el track.** Si una subtarea toca algo de la fila "Crítico", es nivel 3 aunque parezca pequeña.
3. **Escalado:** si un subagente falla dos veces o devuelve tests en rojo, se reasigna al nivel superior. Nunca se baja de nivel una subtarea crítica para ahorrar coste.
4. **Paralelismo:** las subtareas independientes del mismo track se lanzan en paralelo, cada una con `isolation: worktree` si tocan ficheros distintos. Las que comparten ficheros van en secuencia.
5. **Contexto mínimo:** cada subagente recibe solo la subtarea, los ficheros que debe tocar y las secciones de `docs/` que le aplican (de la tabla "Leer" de la ficha), nunca la ficha entera ni el historial.
6. **Cierre:** el PR incluye la tabla "Subtarea · Nivel · Subagente · Escalados", para que Luciano vea qué hizo cada modelo.