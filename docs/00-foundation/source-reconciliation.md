# Reconciliación con los manuales originales

`docs/` deriva de tres manuales: Manual Consolidado (Partes 0–2 y Apéndices A–C), Parte 3 (stack, §13–§21) y Parte 4 (secuencia, §22–§32). Entre ellos hay contradicciones. Este documento dice cómo se resolvió cada una aquí, para que puedas verificarlo. **Para el agente, `docs/` manda.**

## §1 Contradicciones resueltas

| # | Contradicción | Resolución en `docs/` | Base |
|---|---|---|---|
| RC1 | El Manual (cabecera, §1, §2, §8) describe coexistencia con ACL, Strangler Fig de 6 fases y `EJSyncMap` bidireccional con `conflict_rule` | Se aplica el modelo greenfield: ETL one-shot + big-bang. `EJSyncMap` es solo traza unidireccional del ETL (`domain/migration.md`), sin `conflict_rule` | D11, D12, Parte 3 §14 |
| RC2 | Parte 4 cita "Parte 0 §2" como base greenfield, y el Manual en su Parte 0 §2 dice lo contrario | Se toma Parte 3 §14 como fuente de la estrategia de migración | D11 |
| RC3 | El apéndice de Parte 3 lista como abiertos los dos ⚑ de A6 | Cerrados por P6: `svc_solver` con `BYPASSRLS`; `FIELD_CAPTURE` atado a `ResourceAssignment` | P6 |
| RC4 | Faltaba el addendum S9 en Parte 4 | Integrado en las fichas de paso (`principles.md §4`) | P4, P6, P8, P11 |
| RC5 | A2 dice que ATP es "derivada, nunca almacenada"; A7 y Parte 3 la materializan en ClickHouse | Se lee como "nunca fuente de verdad". Hay una proyección materializada en ClickHouse, reconstruible desde Kafka | H6, A7 |
| RC6 | A7 define `EffectiveAccessScope` como vista materializada refrescada por evento, y también exige consistencia transaccional para RLS | Las dos cosas no son compatibles. Además, una vista refrescada por evento no ve las caducidades por `valid_to`. Se evalúa por sentencia, sin materializar | ADR-0004 (Propuesto) |
| RC7 | Parte 4 pone A6 en Fase 0, pero A6 necesita `User`, `LegalEntity`, `BusinessUnit` y `Warehouse` (Capa 1, Fase 1) | Núcleo organizativo adelantado a Fase 0 (paso 0.4) | ADR-0003 (Propuesto) |
| RC8 | A10 nombra los topics de versionado `nexus.masterdata.{tabla}.versioned`; A7 fija `nexus.{capa}.{aggregate}.{evento}` | Se usa `nexus.{contexto}.{aggregate}.versioned` | `conventions-events.md §5` |
| RC9 | El manual no define autenticación (solo la tabla `User`) | Microsoft Entra ID (OIDC), coherente con Azure | ADR-0006 (Propuesto) |

## §2 Lo que `docs/` añade y no está en los manuales

Todo va marcado `[+]` en su sitio. Los bloques grandes:

- Servicios desplegables y esquemas por contexto (`architecture.md`, ADR-0002).
- Convenciones de BD, backend, API, eventos, frontend y tests.
- Columnas técnicas (auditoría, `legal_entity_id` donde el manual no lo listaba, estados) en las DDL de `docs/domain/`.
- Reglas de negocio con id, derivadas de los principios del manual. Cada una indica su origen o va con `[+]`.
- El modelo de planificación del Solver (`domain/solver.md`, ADR-0012): el manual solo fija sus entradas.

## §3 Elementos del manual que siguen siendo reconstrucción

El Apéndice C del Manual avisa de que los campos de Capa 1 (`Party.erp_company_code`, `Group.consolidation_method`), varios campos de `JobTransport` y el contenido de A1–A3 se reconstruyeron desde fragmentos. Se mantienen tal cual. Si detectas una discrepancia con el original, corrígela en `docs/domain/` con un PR `spec/` antes del paso que la implementa.
