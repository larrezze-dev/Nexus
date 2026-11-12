# TAREAS — Plan de desarrollo NEXUS

> Generado desde `docs/steps/STATUS.md` con `python3 tools/gen_tareas.py` (CLAUDE.md §10). Panel visual: `TAREAS.html`.

**Status:** `[ ]` no iniciada · `[o]` en marcha (incluye Gate C / En revisión: entregada pero sin aprobar) · `[x]` finalizada (`Aprobado` por Luciano).

**Resumen:** 7/45 tareas aprobadas (15.6%) · esfuerzo de codificación 19/141 tracks (13.5%) · 1 en marcha.

## Fase 0 · Fundación

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [x] | **0.1** Monorepo, CI y esqueletos (tracks I; estado: Aprobado) | Repositorio único, integración continua y esqueletos de todos los proyectos. | — | Aprobado 2026-11-11. |
| [x] | **0.2** Entorno local (compose + Keycloak) (tracks I; estado: Aprobado) | Entorno local reproducible (contenedores, Keycloak) para desarrollar sin depender de nada externo. | 0.1 | Aprobado 2026-11-11. Bloqueado por: ADR-0006. |
| [x] | **0.3** BD: roles, Flyway, Outbox, UnitOfWork (tracks C B; estado: Aprobado) | Base de datos base: roles, migraciones Flyway, bandeja de salida de eventos (Outbox) y unidad de trabajo. | 0.2 | Aprobado 2026-11-06. Bloqueado por: ADR-0001/2/8/9. |
| [x] | **0.5** Autenticación, Gateway y shell Angular (tracks C B F; estado: Aprobado) | Login, gateway de entrada y carcasa de la aplicación web Angular. | 0.2 | Aprobado 2026-11-11 (C: PR #12 · B: PR #13 · F: PR #14). Deuda: e2e 0.5-login.spec.ts pasa a 1.3; deuda menor del PR #14 anotada en STATUS. Bloqueado por: ADR-0006, ADR-0010. |
| [x] | **0.4** Núcleo organizativo (S10) (tracks C B F; estado: Aprobado) | Núcleo organizativo: empresas, almacenes y estructura sobre la que cuelga todo lo demás. | 0.3, 0.5 | Aprobado 2026-10-30. Bloqueado por: ADR-0003, ORG-Q1, ADR-0014. |
| [x] | **0.6** RBAC + RLS (A6) (tracks C B F; estado: Aprobado) | Roles y permisos (RBAC) y aislamiento de datos por empresa (RLS). | 0.4 | Aprobado 2026-11-06 (5 PRs, 2 ADRs, 2 vulnerabilidades reales corregidas). Bloqueado por: ADR-0004. |
| [x] | **0.7** Relay, Schema Registry, consumidores, DLQ (A7) (tracks C B S; estado: Aprobado) | Relay que publica eventos a Kafka, registro de esquemas, consumidores y cola de errores. | 0.3 | Aprobado 2026-11-08 (C: PR #25, #27, #30 · S: PR #28 · B: PR #29, #31 · Gate D: PR #32; ADR-0017 y ADR-0018 aceptados). Bloqueado por: ADR-0008, ADR-0018. |
| [o] | **0.8** Proyecciones ClickHouse y auditoría (tracks C S F; estado: En revisión) | Proyecciones analíticas en ClickHouse y auditoría de cambios. | 0.7 | C (PR #34), S (PR #35) y ADR-0019 (PR #37, #38) entregados. F entregado: nx-audit-trail (línea de tiempo con diff campo a campo y referencias resueltas), pestaña Auditoría en LE, BU, almacén y usuario, y tokens CSS de la GUI (web/src/styles). Gate D verificado en vivo: created/updated/updated con versiones 1, 2, 3 en < 2 s; rebuild event_log con las mismas cifras. Pendiente la prueba manual y aprobación de Luciano. |
| [ ] | **0.9** Workflow de aprobaciones (A8) (tracks C B S F; estado: Pendiente) | Flujo de aprobaciones reutilizable por el resto de módulos. | 0.6, 0.7 | WF-Q1 y WF-Q2 decididos el 2026-11-11 (auto-aprobación opcional por plantilla; sin plantilla, rechazo). Listo para lanzar. |
| [ ] | **0.10** Gestión documental (A9) (tracks C B F; estado: Pendiente) | Gestión documental: adjuntos y documentos asociados a entidades. | 0.6, 0.7, 0.9 |  |
| [ ] | **0.11** Terraform Azure + Arc (tracks I; estado: Pendiente) | Infraestructura en Azure como código (Terraform) y conexión Arc. | 0.8, 0.9, 0.10 | Bloqueado por: ADR-0011. |

## Fase 1 · Master data: estructura y catálogo

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [ ] | **1.1** Capa 1 completa (tracks C B F; estado: Pendiente) | Resto de maestros de la capa 1 (datos organizativos de base). | 0.6, 0.7 | ADR-0020 aceptado el 2026-11-11 (org.exchange_rate como ImmutableAggregateRoot). Listo para lanzar. |
| [ ] | **1.2** Unidades de medida (A11) + `nexus-catalog` (tracks C B F; estado: Pendiente) | Unidades de medida y conversiones; nace el servicio de catálogo. | 0.6, 0.7 |  |
| [ ] | **1.3** Taxonomía y AssetType (tracks C B S F; estado: Pendiente) | Taxonomía de material y tipos de activo (qué se puede alquilar/mover). | 1.1, 1.2 |  |
| [ ] | **1.4** Versionado (A10): precios y BOM (tracks C B S F; estado: Pendiente) | Precios y composiciones (BOM/kits) con historial de versiones. | 1.3 | CAT-Q1, VER-Q1 y ADR-0020 cerrados el 2026-11-11 (fecha futura en todas las VR1, el futuro es editable, baja como fila). Depende de 1.3. |
| [ ] | **1.5** AssetItem, lotes y proveedores (tracks C B F; estado: Pendiente) | Unidades físicas de material, lotes y proveedores. | 1.3 | CAT-Q4 decidido el 2026-11-11: asset_batch es tabla propia. |
| [ ] | **1.6** Planes de mantenimiento (tracks C B F; estado: Pendiente) | Planes de mantenimiento de los activos. | 1.5 |  |

## Fase 2 · Proyectos y configuración operacional

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [ ] | **2.1** Project y Job + `nexus-operations` (tracks C B S F; estado: Pendiente) | Proyectos y trabajos (eventos); nace el servicio de operaciones. | 1.1 |  |
| [ ] | **2.2** JobLine y JobDeliveryPart (tracks C B F; estado: Pendiente) | Líneas de material de cada trabajo y sus partes de entrega. | 2.1, 1.4 |  |
| [ ] | **2.3** JobTransport y matriz de costes (tracks C B F; estado: Pendiente) | Transportes del trabajo y matriz de costes de transporte. | 2.1 | Bloqueado por: PRJ-Q3. |
| [ ] | **2.4** Reglas operacionales (cierre del input del Solver) (tracks C B F; estado: Pendiente) | Reglas operacionales que completan los datos de entrada del Solver. | 2.2, 2.3 | Bloqueado por: PRJ-Q2, CAT-Q3. |

## Fase 3 · Ledger de stock

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [ ] | **3.1** Ledger StockMovement + saldo (tracks C B S F; estado: Pendiente) | Libro mayor de stock (movimientos y saldos): la fuente de verdad de existencias. | 1.5, 2.1 |  |
| [ ] | **3.2** PurchaseOrder + GoodsReceipt (tracks C B S F; estado: Pendiente) | Pedidos de compra y recepción de mercancía. | 3.1, 0.9 | Bloqueado por: STK-Q1, Q2, Q3. |
| [ ] | **3.3** Inventario físico (tracks C B F; estado: Pendiente) | Inventario físico y ajustes. | 3.1 |  |

## Fase 4 · Transferencias, disponibilidad y Solver

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [ ] | **4.1** StockLocation, Transfer, OperationLog (tracks C B S F; estado: Pendiente) | Ubicaciones, transferencias entre almacenes y registro de operaciones. | 3.1, 2.4 | Bloqueado por: LOG-Q2. |
| [ ] | **4.2** Reservation (tracks C B S F; estado: Pendiente) | Reservas de material para trabajos. | 4.1 | Bloqueado por: LOG-Q1. |
| [ ] | **4.3** AvailabilityProjection (ATP) (tracks C S F; estado: Pendiente) | Disponibilidad prometida (ATP): qué material está libre y cuándo. | 4.2 |  |
| [ ] | **4.4** Solver (isla JVM) (tracks C J; estado: Pendiente) | Solver Timefold (servicio JVM) que optimiza asignación y transferencias. | 2.4, 4.3 | Depende del hito S5 (aprobar 2.4) y del contrato 4.3-C. Bloqueado por: ADR-0012, SLV-Q1. |
| [ ] | **4.5** Propuestas del Solver → Transfers (tracks C B S F; estado: Pendiente) | Propuestas del Solver convertidas en transferencias reales. | 4.3, 4.4 |  |

## Fase 5 · Comercial, RR.HH., facturación y controlling

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [ ] | **5.1** PaymentTerms, Opportunity, Quote + `nexus-commercial` (tracks C B S F; estado: Pendiente) | Ofertas: condiciones de pago, oportunidades y presupuestos; nace el servicio comercial. | 1.4, 0.9, 4.2 | Bloqueado por: COM-Q3, COM-Q4. |
| [ ] | **5.2** Contract, Amendment, instanciación de Project (tracks C B S F; estado: Pendiente) | Contratos, enmiendas y creación de proyectos desde contrato. | 5.1, 2.1 | Bloqueado por: COM-Q1, COM-Q2. |
| [ ] | **5.3** Worker y maestros de RR.HH. + `nexus-resources` (tracks C B F; estado: Pendiente) | Trabajadores y maestros de RR.HH.; nace el servicio de recursos. | 1.1, 0.10 | Bloqueado por: HR-Q2. |
| [ ] | **5.4** Necesidades, asignaciones, horas, gastos (+ `FIELD_CAPTURE`) (tracks C B S F; estado: Pendiente) | Planificación de personal, horas y gastos (captura de campo). | 5.3, 2.1 | Bloqueado por: HR-Q1. |
| [ ] | **5.5** Facturación y exportación ERP (tracks C B S F; estado: Pendiente) | Facturación y exportación al ERP. | 5.2, 3.2, 4.1 | Bloqueado por: BIL-Q1, BIL-Q3. |
| [ ] | **5.6** Catálogo de costes y ProjectBudget + `nexus-controlling` (tracks C B S F; estado: Pendiente) | Catálogo de costes y presupuesto de proyecto; nace el servicio de controlling. | 0.9, 2.1 | Bloqueado por: CTL-Q1, CTL-Q4. |
| [ ] | **5.7** CostPosting y ProjectPnL (tracks C B S F; estado: Pendiente) | Costes reales y cuenta de resultados (P&L) por proyecto. | 5.4, 5.5, 5.6 | Bloqueado por: CTL-Q2. |

## Fase 6 · Transversales y captura móvil

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [ ] | **6.1** AssetIdentifier, LoadSession, ScanEvent + `nexus-field` (tracks C B S F; estado: Pendiente) | Identificadores de activos (códigos, RFID), sesiones de carga y escaneos. | 4.1 | Bloqueado por: WHS-Q1 (solo RFID). |
| [ ] | **6.2** Commissioning (tracks C B S F; estado: Pendiente) | Puesta en marcha (commissioning) de instalaciones. | 6.1, 1.4 | Bloqueado por: CMS-Q1. |
| [ ] | **6.3** Trazabilidad en campo (tracks C B S F; estado: Pendiente) | Trazabilidad del material en campo. | 6.1, 4.1 | Bloqueado por: FLD-Q1. |
| [ ] | **6.4** App móvil: base offline-first y sync (tracks C B M; estado: Pendiente) | Base de la app móvil: offline-first y sincronización. | 6.1 | Bloqueado por: MOB-Q4 (despliegue). |
| [ ] | **6.5** App móvil: flujos y RFID (tracks M; estado: Pendiente) | Flujos de la app móvil y lectura RFID. | 6.1, 6.2, 6.3, 6.4 | Bloqueado por: MOB-Q1. |

## Fase 7 · Migración y cutover

| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |
|---|---|---|---|---|
| [ ] | **7.1** GDPR / PII (A12) (tracks C B S F; estado: Pendiente) | Protección de datos personales (GDPR/PII). | 0.9, 5.3 | Bloqueado por: GDP-Q1…Q4. |
| [ ] | **7.2** ETL: extracción y staging (tracks C B; estado: Pendiente) | ETL: extracción de EasyJob/adunic a staging. | — | Requiere acceso a NUS-SQL-001\NRP. Bloqueado por: acceso a `NUS-SQL-001\NRP`. |
| [ ] | **7.3** ETL: transformación, carga, id_map, reconciliación (tracks C B; estado: Pendiente) | ETL: transformación, carga, mapa de ids y reconciliación. | 7.2 | Bloqueado por: tablas de correspondencia. |
| [ ] | **7.4** Ensayos, piloto opcional, runbook, go/no-go (tracks B I; estado: Pendiente) | Ensayos de migración, runbook y decisión go/no-go del cutover. | 7.1, 7.3, 0.11 | Bloqueado por: MIG-Q1…Q3. |

## Trabajo derivado de decisiones

| Trabajo | Decisión | Tracks | Antes de |
|---|---|---|---|
| `PUT /v1/roles/{id}/permissions` exige `If-Match` (428/409) | 2026-11-11, `domain/access.md §8` | C → B ∥ F | Gate D de 0.9 (siguiente paso que toca `platform.yaml`) |
| Vinculación del `app_user` sin `oid` en el primer login (solo miembros del tenant, solo en `nexus-platform`) | 2026-11-11, ADR-0006 | B | 7.2 (ETL de usuarios) |
| Deuda menor del shell (PR #14): listener de 401 sin conectar, posible bucle de redirección con SSO silencioso, accesibilidad (`<html lang>`, `MatPaginatorIntl`, `aria-live`), ruta comodín `**`, estado de error explícito en `nx-data-grid` | 2026-11-11, aprobación de 0.5 | F | 1.3 (primer paso con pantallas de catálogo) |
| Interfaz web coherente: sistema de diseño (`conventions-frontend.md §4`) aplicado a todas las pantallas existentes; cierra también la accesibilidad, `**` y `MatPaginatorIntl` de la fila anterior. Avance 2026-11-12: spec #49, contrato #47 y backend #48 mergeados; base común web (shell, layout, grid, i18n, rutas de error) en PR-4 con ok de Luciano a F8; pendientes las pantallas de org (PR-5) y de accesos y cuenta (PR-6) | 2026-11-12, canvas aprobado por Luciano | F | 1.3 |
| Lista de entidades legales con `legalName`, `displayName`, `q` e `isActive` | 2026-11-12, `domain/org.md §5` | C → B ∥ F | Cierre de la interfaz coherente |
| `PATCH /v1/me` para guardar el idioma elegido (sin `If-Match`) | 2026-11-12, `conventions-api.md §5` | C → B ∥ F | Cierre de la interfaz coherente |

## Hitos

| Hito | Condición | Confirmado |
|---|---|---|
| Plataforma lista | 0.1–0.10 `Aprobado` | — |
| Master data 1·2·3 completa (S5) | 2.4 `Aprobado` | — |
| Ledger operativo (S6) | 3.1 `Aprobado` | — |
| Planificación con Solver | 4.5 `Aprobado` | — |
| Ciclo comercial-financiero completo | 5.7 `Aprobado` | — |
| Captura edge en producción de prueba | 6.5 `Aprobado` | — |
| Go-live | 7.4 go | — |
