# ADR-0016 · Precisión de los instantes que envía el cliente (sub-microsegundo)

- **Estado:** Aceptado
- **Fecha:** 2026-11-04
- **Decide:** Luciano Arrezze
- **Afecta a:** `Nexus.BuildingBlocks.Application.Validation`, todo comando con un instante de entrada que acabe en un evento (`POST /v1/role-assignments` `validFrom`/`validTo` en 0.6; después reservas, transferencias, etc.), `conventions-api.md` (fila "Instantes")

## Contexto
Los eventos serializan los instantes como Avro `timestamp-micros` (`conventions-events.md`), y `AvroPayloadSerializer` rechaza (no redondea) cualquier valor con más precisión. El reloj del servidor ya está resuelto: `RoundedSystemTimeProvider` trunca "ahora" a microsegundos en origen. Pero los instantes que **manda el cliente** no pasan por ese reloj: ISO-8601 admite 7 decimales (`2026-11-04T10:00:00.1234567Z`), System.Text.Json los conserva, y hoy un `POST /v1/role-assignments` con un `validFrom` así llega al outbox y falla con `EventPayloadSchemaException` (500), no con un 400. Además PostgreSQL `timestamptz` **redondea** al microsegundo más cercano, mientras que cualquier truncado en C# va hacia abajo: el valor guardado y el del evento podrían diferir en 1 µs, y la comparación de idempotencia `existing.ValidFrom == command.ValidFrom` (`CreateRoleAssignment.cs`) daría falso con un reenvío idéntico. El CI de `main` lo puso de manifiesto (los tests mandaban `DateTimeOffset.UtcNow` sin redondear); los tests ya se corrigieron, la API no. `docs/` no dice qué hacer.

## Decisión
**Rechazar en validación.** Un helper compartido `MustBeMicrosecondPrecision()` en `Nexus.BuildingBlocks.Application.Validation` (`Ticks % 10 == 0`, `null` pasa) aplicado a todo instante de entrada que pueda acabar en un evento; devuelve 400 `validation-failed` con el campo. Se documenta en `conventions-api.md`: "Instantes: como máximo 6 decimales". Mismo criterio que `AvroPayloadSerializer` ("rechazar, no redondear en silencio").

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Truncar en el handler/dominio | Altera en silencio un dato del cliente y no coincide con el redondeo de PostgreSQL (diferencia de 1 µs entre BD y evento, idempotencia rota) salvo que se replique exactamente el redondeo de Postgres |
| Redondear en `AvroPayloadSerializer` | Contradice su política explícita; el evento dejaría de ser fiel a lo que se guardó |
| No hacer nada | 500 en vez de 400 ante una entrada válida según ISO-8601 |

## Consecuencias
- Error de cliente claro (400) en vez de un 500 en el outbox; BD, evento e idempotencia ven el mismo valor.
- Un cliente externo que mande 7 decimales tiene que recortarlos. El frontend Angular no se ve afectado (`Date` de JS tiene precisión de milisegundos).
- Cada validador nuevo con instantes de entrada debe usar el helper (se puede cubrir con un test de arquitectura).

## Cómo se verifica
Test de integración: `POST /v1/role-assignments` con `validFrom` de 7 decimales → 400 con el campo `validFrom`; con 6 decimales → 201 y el evento `platform.role-assignment.changed` lleva exactamente ese valor.

## Implementación
Implementado en la rama `spec/access-scope-hardening`:
- `MicrosecondPrecisionValidator<T, TProperty>` (`Nexus.BuildingBlocks.Application/Validation/`) y dos sobrecargas `MustBeMicrosecondPrecision()` en `NexusRuleBuilderExtensions`, para `DateTimeOffset` y `DateTimeOffset?` (`null` pasa). La condición es `Ticks % TimeSpan.TicksPerMicrosecond == 0`, la misma constante que usa `RoundedSystemTimeProvider`. No se proyecta a OpenAPI (JSON Schema no puede expresarla); la documenta `conventions-api.md`.
- `CreateRoleAssignmentValidator` la aplica a `ValidFrom` y `ValidTo` (con `WithName("validFrom")`/`"validTo"`). A día de hoy es el único comando con instantes de entrada que acaban en un evento: los otros `DateTimeOffset` de entrada de Platform (`asOf` en `getRolePermissions`, `activeOn` en `listRoleAssignments`) son filtros de consulta y nunca llegan al outbox.
- Tests: unitarios del helper y del validador (`NexusRuleBuilderExtensionsTests`, `RoleAssignmentValidatorTests`) y los dos de integración de "Cómo se verifica" en `RoleAssignmentApiTests` (7 decimales → 400 `VALIDATION` con `'validFrom'` en el detalle, sin fila ni mensaje en el outbox; 6 decimales → 201 y respuesta, `access.role_assignment.valid_from` y el `validFrom` del evento en `outbox.message` coinciden exactamente). Sin la regla, el primero devuelve 500 (comprobado).
- Los tests de integración que mandaban `DateTimeOffset.UtcNow` sin redondear a `POST /v1/role-assignments` (en `RoleAssignmentApiTests`, `UsersApiTests`, `WarehouseApiTests`, `LegalEntityApiTests`, `PartyApiTests`, `BusinessUnitApiTests`) usan ahora `RoundedSystemTimeProvider.Instance.GetUtcNow()`; eran los 21 fallos (500) del CI en Linux del PR #24.

Detalle que el texto no recogía: el 400 llega por la ruta del `Dispatcher` (`Error.Validation`), así que el campo aparece en `detail` (`"ValidFrom: 'validFrom' must not be more precise…"`), no en un array `errors` por campo; es el mismo formato que el resto de errores de validación de comandos de este servicio.
