# ADR-0020 · Versión de agregado en tablas append-only y VR1

- **Estado:** Propuesto
- **Fecha:** 2026-11-11
- **Decide:** Luciano Arrezze
- **Afecta a:** 1.1 (`org.exchange_rate`), 1.4 (`asset_type_bom`, `asset_type_price_rule`), 5.4 (`hr.role_rate`), 5.6 (`controlling.cost_concept_reserve_pct`), `conventions-database.md §4 y §8`, `conventions-events.md §2`, `domain/versioning.md`

## Contexto

ADR-0019 persiste `aggregate_version` en las tablas de los agregados mutables, pero excluye las append-only y las versionadas VR1 (§5): en ellas cada fila es su propia versión. Sus eventos (`nexus.org.exchange-rate.published` en 1.1, `nexus.<contexto>.<aggregate>.versioned` desde 1.4) siguen necesitando las cabeceras `nexus-aggregate-id` (UUID, clave del mensaje Kafka) y `nexus-aggregate-version` (entero creciente por agregado, `conventions-events.md §2`). Además, `AggregateVersionConvention` (ADR-0019) mapea `AggregateVersion` a una columna `aggregate_version` en todo `AggregateRoot<>`. Una tabla sin esa columna no se puede cargar. Este ADR cierra el ⚑ de `conventions-database.md §4`. Luciano eligió el 2026-11-11 la opción "derivarla de la fila".

Ese mismo día Luciano cerró VER-Q1 (`valid_from` futuro en **todas** las tablas VR1) y su sub-decisión, "el futuro es editable". Este ADR fija cómo encajan las dos decisiones, porque el número de versión de los eventos depende de ambas.

## Decisión

1. **Clase base `ImmutableAggregateRoot<TId>`** (`BuildingBlocks.Domain`), derivada de `AggregateRoot<TId>`. Representa una fila que se inserta una vez y emite **como mucho un evento, en su alta**. `AggregateVersionConvention` no mapea `AggregateVersion` en estos tipos: la ignora en el modelo de EF y no se busca ninguna columna `aggregate_version`. El `UnitOfWork` acepta sus eventos aunque la versión no esté persistida, porque la versión se deriva de la fila (punto 2). Una fila de estas que se carga y se modifica (solo el cierre de VR1) no emite eventos.
2. **De dónde sale la versión del evento:**
   - **Append-only** (p. ej. `org.exchange_rate`): cada fila es un agregado propio. `nexus-aggregate-id` = `id` de la fila; `nexus-aggregate-version` = **1**, siempre.
   - **VR1**: el agregado es la cadena de versiones de un `business_key`. `nexus-aggregate-id` = **`id` de la fila con `version_no = 1`** de ese `business_key` (identificador de linaje: estable, UUID y ya generado por la aplicación, ADR-0009). `nexus-aggregate-version` = **`version_no`** de la fila nueva. El payload sigue llevando `businessKey`.
3. **Todo evento VR1 corresponde a una fila.** Por eso la baja lógica pasa a ser una versión más: una fila con `is_retired = true` que copia los valores de la anterior (columna nueva en la plantilla de `conventions-database.md §8`). Así `version_no` es consecutivo, ningún número de evento se queda sin fila y una baja programada que luego se anula no deja huecos ni duplicados. Volver a dar de alta un `business_key` dado de baja es otra versión (`is_retired = false`) de la misma cadena.
4. **Concurrencia:** la fila nueva solo se inserta. Dos comandos simultáneos sobre el mismo `business_key` chocan en `UNIQUE (business_key, version_no)` o en el `EXCLUDE` de no solapamiento. El perdedor recibe 409 por el camino existente y nunca publica un número de versión duplicado. El paso 1 de `versioning.md §4` (`FOR UPDATE` sobre las filas afectadas) serializa el caso normal.
5. **Versiones anuladas** (VER-R7, "el futuro es editable"): una versión programada que queda anulada mantiene su `version_no`, que nunca se reutiliza. El evento de la versión que la anula lleva `annulledVersionIds`. Los consumidores (proyecciones `*_history`, réplicas) marcan esas versiones como anuladas. La serie de `nexus-aggregate-version` sigue siendo estrictamente creciente.

## Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| No modelarlas como `AggregateRoot<>`: el handler escribe el evento con una API explícita del `OutboxWriter` y calcula la versión a mano | Cada handler calcularía la versión a su manera, fuera del camino único del `UnitOfWork` (regla dura del Outbox). |
| Añadir también `aggregate_version` a estas tablas | Reabre ADR-0019 §5. En VR1 duplicaría `version_no`. En append-only no tiene sentido: la fila nunca se actualiza. |
| `nexus-aggregate-id` = UUIDv5 de (`tabla`, `business_key`) | Introduce un segundo esquema de UUID junto a ADR-0009. El `id` de la versión 1 ya existe y es igual de estable. |
| Baja lógica sin fila (cerrar sin sucesora) y el evento con `version_no + 1` | Si una baja programada se anula después, o el `business_key` vuelve a darse de alta, ese número se repite o hay que guardarlo aparte. Con la fila de baja no hace falta contador. |

## Consecuencias

- 1.1 puede crear `org.exchange_rate` como `ImmutableAggregateRoot<>` sin columna extra.
- La plantilla VR1 gana `is_retired`. Toda consulta de "lo vigente" o "as of" filtra `NOT is_retired` para decidir si el recurso existe en `t` (VER-R5).
- Publicar una versión necesita el `id` de la versión 1 de la cadena. Es una lectura más, indexada por `UNIQUE (business_key, version_no)`.
- `conventions-events.md §2` documenta las dos fuentes de la versión (ADR-0019 para los agregados mutables y este ADR para los inmutables).

## Cómo se verifica

- Test de `BuildingBlocks.Persistence`: un `ImmutableAggregateRoot<>` sin columna `aggregate_version` se carga y se guarda, y su evento sale con versión 1 (append-only) o con `version_no` (VR1).
- Test de `VersionedRepository<T>` (1.4): alta, dos versiones y baja dan las versiones 1, 2, 3 y 4 con el mismo `nexus-aggregate-id`. Volver a dar de alta da la 5.
- Dos versiones concurrentes del mismo `business_key`: una gana y la otra recibe 409, sin versión duplicada en el outbox.
- Versión programada anulada (VER-R7): su `version_no` no se reutiliza y el evento siguiente lleva `annulledVersionIds`.
