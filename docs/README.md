# NEXUS — Documentación ejecutable

Esta carpeta es la especificación que Claude Code ejecuta, paso a paso. Está organizada para que el agente lea solo el tramo que necesita, y para que tú puedas verificar cada paso por separado.

## Cómo está organizada

| Carpeta | Qué contiene | Quién la lee |
|---|---|---|
| `00-foundation/` | Principios, arquitectura y convenciones transversales | El agente, solo las secciones que cita cada paso |
| `adr/` | Decisiones de arquitectura nuevas, necesarias para implementar | Tú las apruebas; el agente las respeta |
| `domain/` | Una especificación por bounded context: DDL, reglas con id, estados, API, eventos y pantallas | El agente, solo el dominio del paso |
| `steps/` | Una ficha autocontenida por paso, más `STATUS.md` | El agente, una por sesión; tú, para verificar |

## Orden de lectura para ti (verificación inicial)

1. `00-foundation/source-reconciliation.md`: qué se resolvió respecto a los manuales.
2. `adr/README.md`: las decisiones en estado **Propuesto** que tienes que aceptar o corregir antes de empezar.
3. `00-foundation/architecture.md`: servicios, esquemas y reglas de comunicación.
4. `steps/STATUS.md`: el plan completo de pasos.
5. Luego, cada ficha de paso justo antes de lanzarlo.

## Convenciones de marcado

| Marca | Significado |
|---|---|
| `[+]` | Elemento **añadido en esta especificación** y que no está en el manual. Requiere tu verificación |
| `⚑` | Decisión nueva abierta; bloquea lo que dependa de ella hasta que la cierres |
| `XXX-Rn` | Regla de negocio con id estable (p. ej. `STK-R2`). Los tests la referencian |
| `§n` | Sección del manual original (Manual Consolidado, Parte 3, Parte 4) |

Todo lo que no lleva `[+]` procede de los manuales aprobados (Partes 0–4, Apéndice A con P1–P12).

## Índice

### 00-foundation
- [principles.md](00-foundation/principles.md): principios y registro de decisiones D/S/P
- [architecture.md](00-foundation/architecture.md): servicios, esquemas, comunicación
- [repo-structure.md](00-foundation/repo-structure.md): estructura del monorepo
- [delivery-process.md](00-foundation/delivery-process.md): gates, ramas, worktrees, PR
- [conventions-database.md](00-foundation/conventions-database.md): PostgreSQL, RLS, append-only, versionado
- [conventions-backend.md](00-foundation/conventions-backend.md): .NET, CQRS, handlers, errores
- [conventions-events.md](00-foundation/conventions-events.md): Outbox, Kafka, Avro, consumidores (A7)
- [conventions-api.md](00-foundation/conventions-api.md): REST, OpenAPI, paginación, errores
- [conventions-frontend.md](00-foundation/conventions-frontend.md): Angular, cliente generado, mocks
- [conventions-testing.md](00-foundation/conventions-testing.md): pirámide de tests, Testcontainers
- [glossary.md](00-foundation/glossary.md): términos y equivalencias EasyJob → NEXUS
- [source-reconciliation.md](00-foundation/source-reconciliation.md): contradicciones resueltas

### domain
| Archivo | Contexto | Origen | Fase |
|---|---|---|---|
| [org.md](domain/org.md) | Estructura organizativa | Capa 1 §3 | 0–1 |
| [access.md](domain/access.md) | RBAC + RLS | A6 | 0 |
| [workflow.md](domain/workflow.md) | Aprobaciones | A8 | 0 |
| [documents.md](domain/documents.md) | Gestión documental | A9 | 0 |
| [versioning.md](domain/versioning.md) | Versionado de master data | A10 | 1 |
| [uom.md](domain/uom.md) | Unidades de medida | A11 | 1 |
| [catalog.md](domain/catalog.md) | Catálogo de activos | Capa 2 §4–§5 | 1 |
| [projects.md](domain/projects.md) | Proyectos, Jobs, configuración operacional | Capa 3 §6 | 2 |
| [stock.md](domain/stock.md) | Ledger, compras, inventario físico | Capa 4 §7 | 3 |
| [logistics.md](domain/logistics.md) | Transferencias, reservas, ATP | A1, A2 | 4 |
| [solver.md](domain/solver.md) | Isla Solver | §17 | 4 |
| [commercial.md](domain/commercial.md) | CRM → Oferta → Contrato | A4 | 5 |
| [hr.md](domain/hr.md) | RR.HH. y recursos | Capa 6 §9 | 5 |
| [billing.md](domain/billing.md) | Facturación y exportación ERP | Capa 5 §8 | 5 |
| [controlling.md](domain/controlling.md) | Kalkulation, costes, P&L | A5 | 5 |
| [warehouse-ops.md](domain/warehouse-ops.md) | Operaciones de almacén | §10 | 6 |
| [commissioning.md](domain/commissioning.md) | Commissioning | §11 | 6 |
| [field.md](domain/field.md) | Trazabilidad en campo | §12 | 6 |
| [mobile.md](domain/mobile.md) | Tier de captura móvil | A3, §18 | 6 |
| [gdpr.md](domain/gdpr.md) | GDPR / PII | A12 | 7 |
| [migration.md](domain/migration.md) | ETL one-shot y cutover | §14, §31 | 7 |

### steps
- [steps/STATUS.md](steps/STATUS.md): los 45 pasos (Fases 0–7) con tracks, dependencias, paralelismo, bloqueos, hitos y preguntas abiertas.
- `steps/F0` … `steps/F7`: una ficha por paso, con lo que el agente lee por track, los entregables, las reglas que debe cubrir con tests, los escenarios de integración y la verificación para ti.
- [steps/_template.md](steps/_template.md): plantilla para pasos nuevos.
- Cómo lanzar las sesiones: [delivery-process.md §7](00-foundation/delivery-process.md).
