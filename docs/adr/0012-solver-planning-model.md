# ADR-0012 · Interfaz y modelo de planificación del Solver

- **Estado:** Propuesto
- **Fecha:** 2026-10-18
- **Afecta a:** `domain/solver.md`, pasos 4.4 y 4.5

## Contexto
El manual fija las entradas del Solver (master data Capas 1–3 + ATP desde ClickHouse), su tecnología (Spring Boot + Timefold, Kotlin) y que se salta RLS con `svc_solver` de solo lectura (P6). No fija qué problema resuelve exactamente ni cómo devuelve resultados. Con un rol de solo lectura, el Solver no puede escribir sus resultados en PostgreSQL.

## Decisión
- **Problema v1:** cubrir la demanda de material de los Jobs en un horizonte (líneas de Job con fechas, por tipo y obra) asignando cantidades desde `StockLocation` de origen. Se minimizan el coste de transporte (matrices de 2.3) y las penalizaciones por déficit. Se respeta el ATP por ventana.
- **Entrada:** `POST /v1/runs` con horizonte, Mandanten y Jobs incluidos. El Solver lee master data de PostgreSQL (`svc_solver`) y ATP de ClickHouse.
- **Salida:** el Solver **no escribe en PostgreSQL**. Publica `nexus.solver.run.completed` con propuestas de transferencia y déficits. `nexus-operations` las consume y crea `Transfer` en estado `DRAFT` para revisión humana (4.5).
- **Estado de la corrida:** en memoria del Solver (`SolverManager` de Timefold) mientras dura, más el evento final. `GET /v1/runs/{id}` mientras está viva; después, el historial está en `logistics.solver_run` (escrito por operations).
- **Ninguna propuesta se ejecuta sola:** siempre pasa por `DRAFT` y confirmación humana.
- Modelo de planificación detallado: `domain/solver.md §3`.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Dar permisos de escritura a `svc_solver` | Contradice P6 (solo lectura) |
| Solver como job batch sin API | No permite lanzar escenarios a demanda desde la UI |
| Ejecutar propuestas automáticamente | Riesgo operativo; las transferencias tienen consecuencias físicas |

## Consecuencias
- El Solver es sustituible: solo depende de contratos (REST + un evento).
- El primer alcance no optimiza flota, personal ni rutas multiparada. Esas extensiones serán ADR posteriores ⚑.

## Cómo se verifica
Tests `ConstraintVerifier` por restricción; test e2e de 4.5: corrida → evento → `Transfer` en `DRAFT`.
