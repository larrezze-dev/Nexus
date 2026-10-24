# Estado de los pasos

Solo Luciano cambia un paso a `Aprobado`. El agente puede poner `En curso`, `Gate C` (contrato listo para revisar) o `En revisión` (todos los tracks entregados).

Estados: `Pendiente` · `En curso` · `Gate C` · `En revisión` · `Aprobado` · `Bloqueado`

**Tracks:** C contrato · B backend · S servicios (consumidores, proyecciones, jobs) · F frontend · I infra/tooling · M móvil · J Solver JVM. `C → (B ∥ S ∥ F)` = primero el contrato; después los demás en paralelo, cada uno en su worktree.
**Paralelo con:** pasos que pueden ejecutarse a la vez en otra sesión, una vez cumplidas sus dependencias.
**Bloqueado por:** ADR que debe estar `Aceptado` o pregunta ⚑ que debe estar cerrada antes de lanzar el paso (ver la ficha y el dominio citado).

## Prerrequisito: ADRs

Antes de lanzar 0.1 deben estar en `Aceptado` los ADR 0001–0010 (`docs/adr/README.md`). El 0011 se cierra dentro de 0.11; el 0012, antes de 4.4.

## Fase 0 · Fundación

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [0.1](F0/0.1-monorepo-ci.md) | Monorepo, CI y esqueletos | I | ADR 0001–0010 | — | — | En revisión | |
| [0.2](F0/0.2-entorno-local.md) | Entorno local (compose + Keycloak) | I | 0.1 | 0.5-F | ADR-0006 | En revisión | |
| [0.3](F0/0.3-base-de-datos.md) | BD: roles, Flyway, Outbox, UnitOfWork | C → B | 0.2 | 0.5 | ADR-0001/2/8/9 | En revisión | |
| [0.4](F0/0.4-nucleo-organizativo.md) | Núcleo organizativo (S10) | C → B ∥ F | 0.3, 0.5 | 0.7 | ADR-0003, ORG-Q1 | Gate C | |
| [0.5](F0/0.5-auth-gateway-shell.md) | Autenticación, Gateway y shell Angular | C → B ∥ F | 0.2 | 0.3 | ADR-0006, ADR-0010 | Pendiente | |
| [0.6](F0/0.6-rbac-rls.md) | RBAC + RLS (A6) | C → B ∥ F | 0.4 | 0.7 | ADR-0004 | Pendiente | |
| [0.7](F0/0.7-outbox-relay-kafka.md) | Relay, Schema Registry, consumidores, DLQ (A7) | C → B ∥ S | 0.3 | 0.4, 0.6 | ADR-0008 | Pendiente | |
| [0.8](F0/0.8-proyecciones-clickhouse.md) | Proyecciones ClickHouse y auditoría | C → S ∥ F | 0.7 | 0.9, 0.10 | — | Pendiente | |
| [0.9](F0/0.9-aprobaciones.md) | Workflow de aprobaciones (A8) | C → B ∥ S ∥ F | 0.6, 0.7 | 0.8, 0.10 | WF-Q1, WF-Q2 | Pendiente | |
| [0.10](F0/0.10-gestion-documental.md) | Gestión documental (A9) | C → B ∥ F | 0.6, 0.7 (0.9 para DOC-R5) | 0.8 | — | Pendiente | |
| [0.11](F0/0.11-terraform-azure.md) | Terraform Azure + Arc | I | 0.8, 0.9, 0.10 | Fase 1 | ADR-0011 | Pendiente | |

## Fase 1 · Master data: estructura y catálogo

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [1.1](F1/1.1-capa1-completa.md) | Capa 1 completa | C → B ∥ F | 0.6, 0.7 | 1.2 | — | Pendiente | |
| [1.2](F1/1.2-unidades-de-medida.md) | Unidades de medida (A11) + `nexus-catalog` | C → B ∥ F | 0.6, 0.7 | 1.1 | — | Pendiente | |
| [1.3](F1/1.3-taxonomia-asset-type.md) | Taxonomía y AssetType | C → B ∥ S ∥ F | 1.1, 1.2 | — | — | Pendiente | |
| [1.4](F1/1.4-versionado-precio-bom.md) | Versionado (A10): precios y BOM | C → B ∥ S ∥ F | 1.3 | 1.5 | CAT-Q1, VER-Q1 | Pendiente | |
| [1.5](F1/1.5-items-lotes-proveedores.md) | AssetItem, lotes y proveedores | C → B ∥ F | 1.3 | 1.4 | CAT-Q4 | Pendiente | |
| [1.6](F1/1.6-mantenimiento.md) | Planes de mantenimiento | C → B ∥ F | 1.5 | 2.1 | — | Pendiente | |

## Fase 2 · Proyectos y configuración operacional

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [2.1](F2/2.1-project-job.md) | Project y Job + `nexus-operations` | C → B ∥ S ∥ F | 1.1 | 1.6 | — | Pendiente | |
| [2.2](F2/2.2-jobline-delivery.md) | JobLine y JobDeliveryPart | C → B ∥ F | 2.1, 1.4 | 2.3 | — | Pendiente | |
| [2.3](F2/2.3-transporte-matriz.md) | JobTransport y matriz de costes | C → B ∥ F | 2.1 | 2.2 | PRJ-Q3 | Pendiente | |
| [2.4](F2/2.4-reglas-operacionales.md) | Reglas operacionales (cierre del input del Solver) | C → B ∥ F | 2.2, 2.3 | — | PRJ-Q2, CAT-Q3 | Pendiente | |

## Fase 3 · Ledger de stock

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [3.1](F3/3.1-ledger-stock.md) | Ledger StockMovement + saldo | C → B ∥ S ∥ F | 1.5, 2.1 | — | — | Pendiente | |
| [3.2](F3/3.2-compras-recepcion.md) | PurchaseOrder + GoodsReceipt | C → B ∥ S ∥ F | 3.1, 0.9 | 3.3 | STK-Q1, Q2, Q3 | Pendiente | |
| [3.3](F3/3.3-inventario-fisico.md) | Inventario físico | C → B ∥ F | 3.1 | 3.2 | — | Pendiente | |

## Fase 4 · Transferencias, disponibilidad y Solver

Puerta A1 + A2: **abierta** (P1, P2). Puerta S5 (master data completa): se abre al aprobar 2.4.

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [4.1](F4/4.1-transferencias.md) | StockLocation, Transfer, OperationLog | C → B ∥ S ∥ F | 3.1, 2.4 | — | LOG-Q2 | Pendiente | |
| [4.2](F4/4.2-reservas.md) | Reservation | C → B ∥ S ∥ F | 4.1 | — | LOG-Q1, CAT-Q1 | Pendiente | |
| [4.3](F4/4.3-atp.md) | AvailabilityProjection (ATP) | C → S ∥ F | 4.2 | 4.4-J | — | Pendiente | |
| [4.4](F4/4.4-solver.md) | Solver (isla JVM) | C → J | hito S5, 4.3-C | 4.3 | ADR-0012, SLV-Q1 | Pendiente | |
| [4.5](F4/4.5-propuestas-solver.md) | Propuestas del Solver → Transfers | C → B ∥ S ∥ F | 4.3, 4.4 | Fase 5 | — | Pendiente | |

## Fase 5 · Comercial, RR.HH., facturación y controlling

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [5.1](F5/5.1-ofertas.md) | PaymentTerms, Opportunity, Quote + `nexus-commercial` | C → B ∥ S ∥ F | 1.4, 0.9, 4.2 | 5.3 | COM-Q3, COM-Q4 | Pendiente | |
| [5.2](F5/5.2-contratos.md) | Contract, Amendment, instanciación de Project | C → B ∥ S ∥ F | 5.1, 2.1 | 5.3 | COM-Q1, COM-Q2 | Pendiente | |
| [5.3](F5/5.3-workers.md) | Worker y maestros de RR.HH. + `nexus-resources` | C → B ∥ F | 1.1, 0.10 | 5.1, 5.2 | HR-Q2 | Pendiente | |
| [5.4](F5/5.4-planificacion-personal.md) | Necesidades, asignaciones, horas, gastos (+ `FIELD_CAPTURE`) | C → B ∥ S ∥ F | 5.3, 2.1 | 5.5 | HR-Q1 | Pendiente | |
| [5.5](F5/5.5-facturacion.md) | Facturación y exportación ERP | C → B ∥ S ∥ F | 5.2, 3.2, 4.1 | 5.4 | BIL-Q1, BIL-Q3 | Pendiente | |
| [5.6](F5/5.6-catalogo-costes-presupuesto.md) | Catálogo de costes y ProjectBudget + `nexus-controlling` | C → B ∥ S ∥ F | 0.9, 2.1 | 5.4, 5.5 | CTL-Q1, CTL-Q4 | Pendiente | |
| [5.7](F5/5.7-costes-reales-pnl.md) | CostPosting y ProjectPnL | C → B ∥ S ∥ F | 5.4, 5.5, 5.6 | — | CTL-Q2 | Pendiente | |

## Fase 6 · Transversales y captura móvil

Puerta A3: **abierta** (P3).

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [6.1](F6/6.1-identificadores-sesiones-carga.md) | AssetIdentifier, LoadSession, ScanEvent + `nexus-field` | C → B ∥ S ∥ F | 4.1 | 6.4-M | WHS-Q1 (solo RFID) | Pendiente | |
| [6.2](F6/6.2-commissioning.md) | Commissioning | C → B ∥ S ∥ F | 6.1, 1.4 | 6.3 | CMS-Q1 | Pendiente | |
| [6.3](F6/6.3-trazabilidad-campo.md) | Trazabilidad en campo | C → B ∥ S ∥ F | 6.1, 4.1 | 6.2 | FLD-Q1 | Pendiente | |
| [6.4](F6/6.4-movil-base.md) | App móvil: base offline-first y sync | C → B ∥ M | 6.1 (B) | 6.2, 6.3 | MOB-Q4 (despliegue) | Pendiente | |
| [6.5](F6/6.5-movil-flujos.md) | App móvil: flujos y RFID | M | 6.1–6.4 | 7.1 | MOB-Q1 | Pendiente | |

## Fase 7 · Migración y cutover

| Paso | Título | Tracks | Depende de | Paralelo con | Bloqueado por | Estado | Aprobado (fecha) |
|---|---|---|---|---|---|---|---|
| [7.1](F7/7.1-gdpr.md) | GDPR / PII (A12) | C → B ∥ S ∥ F | 0.9, 5.3 | 7.2 | GDP-Q1…Q4 | Pendiente | |
| [7.2](F7/7.2-etl-extraccion.md) | ETL: extracción y staging | C → B | contratos F1–F6 | 7.1 | acceso a `NUS-SQL-001\NRP` | Pendiente | |
| [7.3](F7/7.3-etl-transformacion-carga.md) | ETL: transformación, carga, id_map, reconciliación | C → B | 7.2 | — | tablas de correspondencia, CAT-Q1 | Pendiente | |
| [7.4](F7/7.4-cutover.md) | Ensayos, piloto opcional, runbook, go/no-go | I ∥ B | 7.1, 7.3, 0.11 | — | MIG-Q1…Q4 | Pendiente | |

## Hitos

| Hito | Condición | Confirmado por Luciano (fecha) |
|---|---|---|
| Plataforma lista | 0.1–0.10 `Aprobado` | |
| Master data 1·2·3 completa (S5) | 2.4 `Aprobado` | |
| Ledger operativo (S6) | 3.1 `Aprobado` | |
| Planificación con Solver | 4.5 `Aprobado` | |
| Ciclo comercial-financiero completo | 5.7 `Aprobado` | |
| Captura edge en producción de prueba | 6.5 `Aprobado` | |
| Go-live | 7.4 go | |

## Preguntas abiertas ⚑ por paso

Cerrarlas a tiempo es lo que mantiene el ritmo: cada una bloquea el paso indicado. Las respuestas se escriben en el dominio correspondiente con un PR `spec/`.

| Pregunta | Dónde | Bloquea |
|---|---|---|
| ORG-Q1 visibilidad del directorio `Party` | `domain/org.md §8` | 0.4 |
| WF-Q1 segregación de funciones · WF-Q2 sin plantilla activa | `domain/workflow.md §9` | 0.9 |
| ADR-0011 hosting de Kafka/ClickHouse, región, edge | `adr/0011` | 0.11 |
| CAT-Q1 semántica de kits `VIRTUAL`/`ASSEMBLY` | `domain/catalog.md §9` | 1.4, 4.2, 7.3 |
| VER-Q1 `valid_from` futuro en tarifas | `domain/versioning.md §9` | 1.4 |
| CAT-Q4 lote como tabla propia | `domain/catalog.md §9` | 1.5 |
| PRJ-Q3 campos reconstruidos de `JobTransport` | `domain/projects.md §9` | 2.3 |
| PRJ-Q2 transferencia entre LE sin regla · CAT-Q3 valoración del alquiler interno | `projects.md §9`, `catalog.md §9` | 2.4 |
| STK-Q1 subalquiler · STK-Q2 compra para otra LE · STK-Q3 tolerancia | `domain/stock.md §9` | 3.2 |
| LOG-Q2 custodio en tránsito | `domain/logistics.md §10` | 4.1 |
| LOG-Q1 reservar sin bloquear | `domain/logistics.md §10` | 4.2 |
| ADR-0012 modelo del Solver · SLV-Q1 reservas `SOFT` | `adr/0012`, `domain/solver.md §10` | 4.4 |
| COM-Q3 PDF de oferta · COM-Q4 etapas | `domain/commercial.md §9` | 5.1 |
| COM-Q1 enmiendas con reducciones · COM-Q2 contrato firmado obligatorio | `domain/commercial.md §9` | 5.2 |
| HR-Q2 alta de persona nueva | `domain/hr.md §9` | 5.3 |
| HR-Q1 aprobación de horas sin A8 | `domain/hr.md §9` | 5.4 |
| BIL-Q1 formatos ERP y plan de cuentas (Finanzas) · BIL-Q3 redondeo/QR | `domain/billing.md §9` | 5.5 |
| CTL-Q1 fase `EVENT` · CTL-Q4 traducciones | `domain/controlling.md §10` | 5.6 |
| CTL-Q2 valoración de consumibles de stock | `domain/controlling.md §10` | 5.7 |
| WHS-Q1 codificación EPC | `domain/warehouse-ops.md §9` | 6.1 (RFID) |
| CMS-Q1 plantilla de fases | `domain/commissioning.md §9` | 6.2 |
| FLD-Q1 conciliación automática | `domain/field.md §9` | 6.3 |
| MOB-Q1 PDA y SDK RFID | `domain/mobile.md §8` | 6.5 |
| GDP-Q1 retención (Legal) · GDP-Q2 aprobador · GDP-Q3 identidad de servicio · GDP-Q4 PII en Kafka | `domain/gdpr.md §9` | 7.1 |
| MIG-Q1 histórico · MIG-Q2 facturas abiertas · MIG-Q3 piloto · MIG-Q4 vinculación de usuarios | `domain/migration.md §8` | 7.4 (MIG-Q1/Q2 condicionan 7.3) |
