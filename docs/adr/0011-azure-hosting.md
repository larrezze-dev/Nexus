# ADR-0011 · Hosting en Azure (cómputo, Kafka, ClickHouse)

- **Estado:** Propuesto ⚑ abierto — se cierra dentro del paso 0.11
- **Fecha:** 2026-10-18
- **Afecta a:** 0.11, `conventions-events.md §5`

## Contexto
D10 fija Azure híbrido + Arc + Terraform, y Parte 3 §20 dibuja los servicios en AKS. No está decidido cómo se hospedan Kafka y ClickHouse, que no tienen servicio nativo de Azure con Schema Registry de Confluent.

## Decisión propuesta (a confirmar en 0.11)
| Componente | Propuesta | Alternativa |
|---|---|---|
| Servicios .NET + Solver | AKS | Azure Container Apps (más simple, menos control de red) |
| PostgreSQL | Azure Database for PostgreSQL Flexible Server, HA zonal | — |
| Kafka + Schema Registry | Confluent Cloud en Azure (Marketplace), red privada | Strimzi en AKS (sin coste de licencia, más operación) |
| ClickHouse | ClickHouse Cloud en Azure | ClickHouse Operator en AKS |
| Blob | Azure Storage (Blob) | — |
| Secretos | Azure Key Vault + Workload Identity | — |
| Edge (almacenes, lectores RFID) | Azure Arc sobre Kubernetes en sitio | — |
| Observabilidad | Azure Monitor + Application Insights (OTLP) | — |

## Preguntas abiertas ⚑
1. ¿Hay acuerdo marco con Confluent o ClickHouse, o se prefiere operar todo en AKS?
2. Coste de retención infinita de topics (`conventions-events.md §5`) en la opción elegida.
3. ¿Qué cargas corren en el edge vía Arc? Propuesta: ninguna en Fase 0; en Fase 6, un *gateway* de lectores RFID fijos si los hay.
4. Región Azure (Suiza Norte por residencia de datos).

## Cómo se verifica
`terraform plan` limpio en `envs/dev`; despliegue de dev con los mismos tests e2e que en local.
