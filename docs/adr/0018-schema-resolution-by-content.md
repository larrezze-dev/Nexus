# ADR-0018 · Resolución del esquema Avro del relay por contenido, no por versión del subject

- **Estado:** Aceptado
- **Fecha:** 2026-11-07
- **Decide:** Luciano Arrezze
- **Afecta a:** `conventions-events.md §2, §6`, paso 0.7 (track B, entregable 4 del relay; track C, `tools/register-schemas.sh`), imagen y servicio compose de `nexus-relay`, todo paso futuro que añada o evolucione un `.avsc`

## Contexto

`outbox.message.schema_version` (y la cabecera `nexus-schema-version`) es el `v<n>` del fichero `.avsc` (`conventions-events.md §2`) contra el que el `OutboxWriter` validó el payload (EV-R2). La ficha 0.7 decía que el relay convierte el JSON de Avro a binario con el esquema del subject `<topic>-value` "versión `schema_version`", es decir, usando el `v<n>` del fichero como número de versión del subject en el Schema Registry. Esos dos números no coinciden en general, así que el relay serializaría con un esquema que no es el que validó el escritor, o fallaría al no encontrar la versión. Las ADR-0008 (Avro + Schema Registry) y ADR-0017 (identidad del consumidor en el `UnitOfWork`, mismo paso 0.7) no lo cubren.

## Decisión

1. `outbox.message.schema_version` es el `v<n>` del **fichero** `contracts/events/<contexto>/<aggregate>.<evento>.v<n>.avsc`, independiente del número de versión que el Schema Registry asigne dentro del subject.
2. El relay resuelve el esquema de escritura **por contenido**: carga el mismo `.avsc` local que validó el `OutboxWriter` (misma convención de ruta que `FileSystemEventSchemaProvider`, a partir de `topic`/`event_type` y `schema_version`) y pide al registry el id de ese esquema exacto con `POST /subjects/<topic>-value` (en Confluent .NET, `LookupSchemaAsync`). Nunca lo resuelve por número de versión del subject (`GET /subjects/<topic>-value/versions/<n>`).
3. Los `.avsc` viajan con el relay: la imagen y el servicio compose de `nexus-relay` llevan `contracts/events`.
4. Una fila cuyo `schema_version` no tiene fichero `.avsc`, o cuyo contenido no está registrado en el subject, falla **por fila** con un error explícito (`last_error` dice qué fichero o qué subject), no como fallo transitorio de infraestructura.
5. `tools/register-schemas.sh` verifica el registro por la misma consulta de contenido.

## Por qué

1. Un cambio **compatible** in situ (añadir un campo con `default`, D8 BACKWARD) edita el mismo fichero `v<n>.avsc` y crea una versión nueva del subject sin renombrar el fichero: `v1.avsc` puede ser la versión 2 o 3 del subject.
2. Un cambio **incompatible** crea `v<n+1>` en un **topic nuevo** (`...posted.v2`, `conventions-events.md §6`), cuyo subject `<topic>.v2-value` empieza en la versión 1 mientras `schema_version` vale 2.
3. `register-schemas.sh` registra solo el fichero al que apunta `contracts/events/topics.yaml`, y volver a registrar un esquema idéntico no crea versión nueva: la numeración del subject depende de la historia de registros, no del nombre del fichero.

## Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Resolver por número de versión del subject = `schema_version` (texto original de la ficha 0.7) | Falla en los tres casos anteriores: serializa con otro esquema o no encuentra la versión. |
| Registrar cada `v<n>` en orden para que la numeración del subject coincida con la del fichero | Frágil por los mismos motivos: un cambio compatible in situ desplaza la numeración, un topic nuevo reinicia en 1 y un registro idempotente no crea versión. Exige además disciplina manual sobre el orden de registro en cada entorno. |
| Usar siempre la última versión del subject (`latest`) | Rompe EV-R2: una fila escrita con un esquema anterior se serializaría con otro distinto del que la validó. |

## Consecuencias

- El relay serializa exactamente con el esquema que validó el escritor, sea cual sea la historia del subject.
- La imagen y el compose del relay dependen de `contracts/events`; un despliegue sin los `.avsc` deja todas las filas en error por fila (visible en `last_error` y en `nexus_outbox_failed_total`).
- Un `.avsc` modificado en el repo pero no registrado produce errores por fila hasta que `register-schemas.sh` lo registre; se detecta en el arranque del entorno, no en silencio.
- Una consulta al registry por esquema distinto; el cliente de Confluent la cachea por contenido, así que el coste es una llamada por esquema y proceso.

## Cómo se verifica

- Test del relay (track B): un subject con dos versiones registradas (la segunda, compatible, sobre el mismo `v1.avsc`) y una fila con `schema_version = 1` cuyo payload se validó contra el fichero actual → se publica con el id del esquema actual, no con el de la versión 1 del subject.
- Test del relay: fila con `schema_version` sin fichero, y fila con fichero no registrado → error por fila con mensaje explícito; las filas de otros agregados del mismo lote se publican.
- `register-schemas.sh` (track C): tras registrar, la consulta por contenido de cada fichero de `topics.yaml` devuelve un id.
