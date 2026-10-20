# Principios y registro de decisiones

## §1 Principios del stack (Parte 3 §13)

1. **Greenfield autónomo.** NEXUS no convive con EasyJob en runtime. EasyJob solo es origen de un ETL offline de una sola pasada (Fase 7).
2. **.NET por defecto, políglota por excepción justificada.** Hay dos excepciones: la isla JVM del Solver y la app Android nativa. Ambas en Kotlin.
3. **Event-driven + CQRS sobre Kafka.** PostgreSQL para escritura (OLTP), ClickHouse para lectura analítica. La consistencia entre ambos se propaga por eventos, nunca por escritura dual.
4. **Append-only y derivación.** Los hechos se asientan, no se editan. Las vistas de negocio se derivan.

## §2 Principios de la secuencia (Parte 4 §22)

1. Greenfield de extremo a extremo; migración de una sola vez al final.
2. **Master data antes que Solver** (constraint duro): Capas 1–3 completas antes de construir el Solver.
3. **Ledger antes que transversales** (constraint duro): Capa 4 operativa antes de §10–§12 y de A1.
4. Plataforma transversal primero: RBAC+RLS (A6), bus/Outbox (A7), aprobaciones (A8) y documental (A9) en Fase 0.
5. **Vertical slices event-driven**: cada paso entrega `comando → PostgreSQL + Outbox → Kafka → proyección`.
6. Append-only desde el día uno.
7. Ejecución con Claude Code; infraestructura con Terraform sobre Azure híbrido + Azure Arc.

## §3 Principio añadido: contract-first y tracks paralelos `[+]`

Cada paso empieza fijando sus contratos (DDL, OpenAPI, Avro). Una vez aprobados, backend, servicios y frontend se desarrollan a la vez contra esos contratos. El frontend trabaja contra un mock generado del OpenAPI. Ver ADR-0005.

## §4 Registro de decisiones cerradas

Estas decisiones **no se reabren**. Si una tarea parece contradecirlas, el agente para y pregunta.

### Stack (D1–D13, Parte 3 §21)

| # | Decisión |
|---|---|
| D1 | C#/.NET en todos los servicios de negocio |
| D2 | Isla JVM Spring Boot + Timefold como única excepción de servidor, acotada al Solver |
| D3 | App móvil Android nativa / Kotlin; MAUI descartado (SDK UHF RFID Android-only) |
| D4 | Kotlin compartido entre Solver y app Android |
| D5 | Frontend web Angular / TypeScript |
| D6 | PostgreSQL para OLTP (fuente de verdad, RLS nativo, Outbox) |
| D7 | ClickHouse para analítica (ATP, P&L) |
| D8 | Kafka + Confluent Schema Registry |
| D9 | Stream processing en .NET; nunca Kafka Streams |
| D10 | Azure híbrido + Azure Arc + Terraform |
| D11 | Greenfield autónomo: sin ACL, sin Strangler Fig, sin CDC, sin `EJSyncMap` de runtime |
| D12 | Migración ETL one-shot offline + big-bang cutover (piloto opcional) |
| D13 | Numeración: Parte 3 = §13–§21; Parte 4 = §22–§32 |

### Secuencia (S1–S8, Parte 4 §32)

| # | Decisión |
|---|---|
| S1 | Modelo de 8 fases (0–7) |
| S2 | Fase 4 bloqueada por A1+A2 → **desbloqueada** (P1, P2) |
| S3 | Fase 6 bloqueada por A3 → **desbloqueada** (P3) |
| S4 | A6/A7/A8/A9 en Fase 0 |
| S5 | Master data 1·2·3 completa antes del Solver |
| S6 | Ledger antes que transversales y transferencias |
| S7 | Migración como última fase |
| S8 | Numeración §22–§32 |

### Promoción del Apéndice A (P1–P12)

| # | Candidato | Resolución que afecta a la implementación |
|---|---|---|
| P1 | A1 Transferencias | Sin cambios |
| P2 | A2 Reservation/ATP | Sin cambios |
| P3 | A3 Captura móvil | Auto-provisión resuelta en P6 |
| P4 | A4 Comercial | Tabla `PaymentTerms` + `Contract.payment_terms_id`; `cancellation_policy_text` + `cancellation_notice_days`; `ContractAmendment.delta_lines` en JSON |
| P5 | A5 Controlling | `AssetType.is_consumable` boolean, default `false` |
| P6 | A6 RBAC+RLS | Rol `svc_solver` con `BYPASSRLS`; `FIELD_CAPTURE` auto-provisionado al crear `ResourceAssignment` |
| P7 | A7 Bus de eventos | Sin cambios |
| P8 | A8 Aprobaciones | `quorum_count` NOT NULL si `QUORUM`; `expires_at` manual + job, nunca auto-aprueba; `DELEGATE` + `delegated_to_user_id` |
| P9 | A9 Documental | Cuatro elementos confirmados |
| P10 | A10 Versionado | `superseded_by_id`; BOM histórico por consulta "as of" |
| P11 | A11 UoM | `NULL` en `stock_uom_id`/`sales_uom_id`/`uom_id` = unidad implícita, sin conversión |
| P12 | A12 GDPR | Tres elementos confirmados |

### Addendum S9 (integrado aquí) `[+]`

Las fichas de pasos ya incluyen los cuatro artefactos que faltaban en las tablas de Parte 4:

| Artefacto | Paso |
|---|---|
| Rol `svc_solver` (`BYPASSRLS`) | 0.3 (creación) y 0.6 (uso documentado) |
| `ApprovalAction.delegated_to_user_id` | 0.9 |
| Regla UoM `NULL` = unidad implícita | 1.2 y 1.3 |
| Tabla `PaymentTerms` | 5.1 |

## §5 Decisiones nuevas de implementación (ADR)

Las decisiones que la implementación necesita y los manuales no cubren están en `docs/adr/`. Mientras un ADR esté en `Propuesto`, nada que dependa de él se implementa. Índice: `docs/adr/README.md`.
