# ADR-0005 · Contract-first y tracks paralelos

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** `delivery-process.md`, todas las fichas de paso

## Contexto
Se quiere desarrollar a la vez backend, servicios y frontend, con verificación humana paso a paso, y con un agente que solo lea el tramo que necesita.

## Decisión
- Cada paso empieza por el **track C**: DDL (`db/migrations`), OpenAPI (`contracts/openapi`) y Avro (`contracts/events`). Termina en **Gate C** (aprobación de Luciano).
- Tras Gate C, los tracks **B** (backend), **S** (servicios) y **F** (frontend) se ejecutan en paralelo, en ramas y worktrees separados, contra el contrato fusionado en `main`.
- El frontend trabaja contra un mock Prism del contrato. El backend se valida contra el contrato con oasdiff. Los consumidores, contra los `.avsc`.
- Un contrato aprobado no cambia durante el paso. Si hace falta, vuelve a Gate C.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Code-first (OpenAPI generado del backend) | El frontend espera al backend; no hay paralelismo |
| Un solo track secuencial por paso | Más lento y con PR enormes, difíciles de revisar |

## Consecuencias
- Dos puntos de revisión por paso, ambos pequeños.
- El contrato se diseña antes de conocer los detalles de implementación; algunos pasos volverán a Gate C. Es un coste aceptado.

## Cómo se verifica
Job `conformance` de CI; ningún PR de track B/S/F modifica `contracts/` ni `db/migrations/`.
