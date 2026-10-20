# ADR-0009 · UUIDv7 generados en aplicación

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** todas las tablas, eventos y la app móvil

## Contexto
Hace falta un tipo de identificador. La app móvil crea registros sin conexión y los sincroniza de forma idempotente (A3), así que el cliente tiene que poder generar ids. PostgreSQL 17 no genera UUIDv7 de forma nativa.

## Decisión
- Todas las claves primarias son `uuid` versión 7, generados por quien crea el registro: servicio .NET (`Guid.CreateVersion7`), app móvil o Solver.
- La base no tiene `DEFAULT` en las PK.
- `event_id` del Outbox también es UUIDv7.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| `bigint identity` | El móvil no puede generar ids offline; los ids secuenciales filtran volumen de negocio |
| UUIDv4 | Inserciones dispersas en índices B-tree; peor localidad |

## Consecuencias
- Inserción ordenada en índices; ids ordenables por tiempo aproximado.
- Hay que validar en el servidor que los ids recibidos del cliente son UUIDv7 bien formados.

## Cómo se verifica
Test de dominio: la fábrica de ids devuelve versión 7. `CHECK` opcional en tablas sincronizadas desde móvil: `uuid_extract_version(id) = 7` (disponible en PostgreSQL 17).
