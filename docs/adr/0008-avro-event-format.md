# ADR-0008 · Avro como formato de eventos

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** `conventions-events.md`, 0.7

## Contexto
D8 fija Confluent Schema Registry con "Avro/Protobuf" y compatibilidad backward. Hay que elegir uno.

## Decisión
- **Avro**, con `TopicNameStrategy` y compatibilidad `BACKWARD`.
- El envoltorio A7 viaja en cabeceras Kafka; el payload, en Avro (`conventions-events.md §2`).
- El Outbox guarda el payload en codificación JSON de Avro; el relay lo convierte a binario.
- Clases generadas: C# con `avrogen` (Apache.Avro.Tools) en `Nexus.Contracts.Events`; Kotlin con el plugin Gradle de Avro en `solver/`.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Protobuf | Igual de válido; Avro encaja mejor con ClickHouse (formato `AvroConfluent` nativo) y con la evolución por defaults |
| JSON Schema | Más pesado en el cable y con reglas de compatibilidad más débiles |

## Consecuencias
- Los esquemas se escriben a mano en `.avsc` y se revisan en Gate C.
- Las enumeraciones se modelan como `string` para no romper consumidores antiguos.

## Cómo se verifica
CI registra cada `.avsc` en un Schema Registry local y comprueba la compatibilidad con la versión anterior del mismo subject.
