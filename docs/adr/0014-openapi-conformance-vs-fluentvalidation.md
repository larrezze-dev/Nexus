# ADR-0014 · Conformidad OpenAPI del servicio vs. reglas de FluentValidation

- **Estado:** Aceptado
- **Fecha:** 2026-10-29 (decidido 2026-10-29)
- **Decide:** Luciano Arrezze
- **Afecta a:** `.github/workflows/ci.yml` (job `conformance`), `tools/oasdiff-err-ignore.txt`, `src/BuildingBlocks/Nexus.BuildingBlocks.Web/OpenApi/` (`PermissionOpenApiOperationTransformer` y hermanos), todo paso con endpoints validados por FluentValidation — en la práctica, 0.4 en adelante

## Contexto

El job `conformance` (añadido en 0.5 track B5) arranca el servicio real, descarga su `/openapi/v1.json` y ejecuta `oasdiff breaking` contra `contracts/openapi/<servicio>.yaml`, con dirección `base = lo que el servicio expone hoy` / `revision = el contrato completo` — así las operaciones que el contrato ya describe pero el servicio aún no implementa son "adiciones" (nunca rompen). En 0.5 esto solo cubría `GET /health` y `GET /v1/me`, dos endpoints sin parámetros ni cuerpo; la única discrepancia encontrada fue de seguridad (documentada en `tools/oasdiff-err-ignore.txt`): `PermissionOpenApiOperationTransformer` escribe `x-permission` pero nunca `security`/`securitySchemes` estándar, así que oasdiff lee el documento del servicio como "este endpoint permite acceso anónimo" aunque en tiempo de ejecución esté protegido por middleware.

0.4 track B es el primer paso con endpoints reales validados por FluentValidation (paginación `page`/`pageSize`, `sort`, filtros de enum, `captionI18n` con `de` obligatorio, patrones de código, etc.). Al ejecutar el mismo job localmente contra el servicio real con los 8 agregados de 0.4 ya registrados: **214 hallazgos `error` de oasdiff** (antes había 1), repartidos en estas categorías — todas con la misma causa raíz:

| Categoría oasdiff | Causa |
|---|---|
| `api-security-anonymous-access-removed` (36) | Mismo gap ya documentado para `/v1/me`, ahora en cada endpoint nuevo |
| `response-property-became-optional` (130) | El generador de OpenAPI de ASP.NET Core (`Microsoft.AspNetCore.OpenApi`, usado por `AddNexusOpenApi`/`MapOpenApi`) deriva el esquema por reflexión sobre los DTOs en C#; no lee las reglas de los `AbstractValidator<T>` de FluentValidation, así que ninguna propiedad aparece como `required` salvo que la nulabilidad de C# ya la implique |
| `request-parameter-min-set` / `-max-set` (24) | `RuleFor(q => q.Page).GreaterThanOrEqualTo(1)` / `InclusiveBetween(1, MaxPageSize)` no se proyectan al `minimum`/`maximum` del parámetro en el esquema generado |
| `request-property-became-enum` / `request-parameter-became-enum` (6) | `.Must(value => value is "ORGANIZATION" or "PERSON")` no se proyecta a un `enum:` |
| `request-property-pattern-added` / `-parameter-pattern-added` (10) | Patrones de código (`^[A-Z0-9_\-\.]{1,40}$`, etc.) validados en el handler, invisibles al generador |
| `new-required-request-property` (4) | P. ej. `captionI18n/de` — el `MustHaveGermanCaption()` custom de FluentValidation no tiene equivalente de esquema |
| `response-property-type-changed` / `-pattern-removed` (4) | Variantes de los anteriores en las respuestas |

Ninguno de estos 214 hallazgos es una regresión real de forma: el contrato (`contracts/openapi/platform.yaml`) describe exactamente lo que los handlers ya validan en tiempo de ejecución (confirmado leyendo cada validador citado arriba). El problema es que el documento que el servicio **genera y publica** en `/openapi/v1.json` no lo refleja, y el job `conformance` compara literalmente ese documento generado, no el comportamiento real — el mismo patrón que ya motivó la nota en `tools/oasdiff-err-ignore.txt` para la seguridad, ahora a una escala que una lista de excepciones manual no puede seguir absorbiendo línea a línea sin perder su propósito (el propio encabezado del fichero advierte contra "una línea para hacer desaparecer un hallazgo no examinado uno a uno"): cada paso futuro con FluentValidation añadirá su propio lote de hallazgos idénticos en naturaleza.

## Decisión

Construir un transformador de OpenAPI (`IOpenApiOperationTransformer`/`IOpenApiSchemaTransformer`, mismo mecanismo que ya usa `PermissionOpenApiOperationTransformer` en `Nexus.BuildingBlocks.Web/OpenApi/`) que inspeccione los `AbstractValidator<T>` de FluentValidation registrados para cada tipo de petición/respuesta y proyecte sus reglas al esquema generado: `RuleFor(...).GreaterThanOrEqualTo`/`InclusiveBetween` → `minimum`/`maximum`; `.Must(value => value is "A" or "B")` sobre una lista fija → `enum`; una regla de "no vacío"/presencia de una clave fija (como `MustHaveGermanCaption`) → `required`; patrones de regex ya usados en `.Matches(...)` → `pattern`. Esto cierra el gap de raíz, una vez, en el building block compartido — en vez de en cada servicio o en una lista de excepciones que crece sin límite.

Alcance explícito: esta decisión cubre solo los hallazgos de **forma** (las categorías de la tabla de arriba salvo `api-security-anonymous-access-removed`). El gap de seguridad (`x-permission` sin `security`/`securitySchemes` estándar) es un problema distinto, ya aceptado desde 0.5 para `GET /v1/me` vía una línea en `tools/oasdiff-err-ignore.txt` cuyo propio encabezado ya anticipaba "cada endpoint protegido solo por `x-permission` necesitará su propia línea hasta que esa corrección llegue" — así que las 36 líneas nuevas de seguridad para los endpoints de 0.4 siguen ese patrón ya aceptado, no abren una decisión nueva.

## Alternativas descartadas (a valorar)

| Alternativa | Por qué no (o por qué sí, a decidir) |
|---|---|
| **Seguir ampliando `tools/oasdiff-err-ignore.txt`** | Escala mal (214 líneas ya, crecería con cada paso) y el propio fichero documenta por qué eso no es su propósito: dejaría de ser una lista de excepciones *examinadas* y pasaría a ser una supresión en bloque. |
| **Debilitar el job `conformance`** (bajar de severidad las categorías `request-parameter-min-set`, `response-property-became-optional`, etc., con `--severity-levels`) | Más simple de implementar ya, pero pierde la capacidad de detectar una regresión *real* de forma en esas mismas categorías (p. ej. alguien quita un `required` real del contrato por error) — el job dejaría de proteger justo lo que más cambia paso a paso. |
| **No generar el documento con reflexión; mantener `platform.yaml` como la única fuente servida** (el servicio sirve el YAML del contrato directamente en `/openapi/v1.json`, en vez de generarlo) | Elimina el gap de raíz (ya no hay dos documentos que puedan discrepar), pero cambia el propósito del job — ya no verificaría que el servicio *implementa* lo que el documento describe, solo que el fichero existe. Contradice la idea original del job (detectar que el servicio implementado se desvía del contrato). |

## Consecuencias

- Un único punto de mantenimiento (el transformer) en vez de una lista de excepciones por endpoint; cada paso futuro con FluentValidation se beneficia automáticamente, sin trabajo adicional en su propio track B.
- Coste: trabajo nivel 3 (nx-architect) en un building block compartido, antes de que 0.4 track B pueda cerrar su job `conformance` en verde.
- Las 36 líneas de seguridad (`api-security-anonymous-access-removed`) para los endpoints nuevos de 0.4 sí se añaden a `tools/oasdiff-err-ignore.txt`, siguiendo el patrón ya aceptado para `GET /v1/me` — no requieren una decisión nueva.

## Cómo se verifica

Con la decisión tomada: `dotnet run` real del servicio + `oasdiff breaking` contra `contracts/openapi/platform.yaml` (igual que este ADR lo reprodujo) vuelve a 0 hallazgos `error` nuevos (solo el ya aceptado de `/v1/me`, o ninguno si el transformer también resuelve seguridad).

## Refinamiento de implementación (2026-10-29, nx-architect)

Añadido tras implementar la decisión; no la reabre. Reproducido contra el servicio real con la receta de "Cómo se verifica" (`oasdiff` `tufin/oasdiff:v1.35.0-rc.1`).

### Corrección del diagnóstico de "Contexto"

Al reproducir los hallazgos uno a uno, dos categorías de la tabla no tienen la causa que se les atribuyó:

- **`response-property-became-optional` (130) no viene de FluentValidation.** Es la dirección contraria: los DTO de respuesta son `record` posicionales, el generador marca *todas* sus propiedades como `required` (y el servicio las escribe siempre, también las `null`: no hay `WhenWritingNull`), mientras el contrato deja fuera de `required` las nulables y `createdAt`/`updatedAt` (p. ej. `BusinessUnit.required: [id, partyId, legalEntityId, code, captionI18n, isActive]`). El documento generado es exacto; el contrato es más laxo que el servicio en las respuestas. Ningún transformer puede cerrarlo sin hacer que el documento generado mienta. ⚑ **Pendiente de decisión de Luciano** (ver más abajo).
- **`response-property-type-changed`/`-pattern-removed` (4, `minorUnits` de `Currency`) tampoco.** Viene de `JsonSerializerDefaults.Web` (`AllowReadingFromString`): el generador describe todo número como `[integer, string]` + patrón, cierto para la entrada pero falso para la salida (el servicio siempre escribe un número JSON). Se cierra la parte de tipo/patrón (abajo); queda `format: int16` (servicio, `short`) frente a sin `format` (contrato): mismo caso que el anterior, contrato más laxo en respuesta.

### Mecanismo implementado

- **`.ValidatedBy<TRequest>()`** (`Nexus.BuildingBlocks.Web.OpenApi`, mismo patrón que `RequirePermission`/`PermissionMetadata`): metadato de endpoint que declara qué comando/consulta valida su entrada. Hace falta **también para los cuerpos**, no solo para los parámetros de consulta: en este código ningún endpoint recibe el comando como cuerpo (reciben un DTO `*Request` del API y construyen el comando a mano), así que no hay ningún tipo CLR del que deducir el validador. Contrato de la declaración: cada miembro con reglas recibe sin transformar el valor del parámetro de ruta/consulta o de la propiedad del cuerpo con el mismo nombre (sin distinguir mayúsculas).
- **`FluentValidationOpenApiOperationTransformer`**: resuelve `IValidator<TRequest>` del DI (igual que el dispatcher), lee las reglas con `CreateDescriptor()` y las proyecta por nombre a parámetros (`path`/`query`) y, si no, a propiedades del esquema del cuerpo (el componente, vía su referencia). Falla en la generación del documento si se declara un validador no registrado o si dos endpoints con validadores distintos comparten DTO de cuerpo.
- **Mapeo:** `GreaterThan[OrEqualTo]`/`LessThan[OrEqualTo]` con constante → `[exclusive]minimum`/`[exclusive]maximum`; `InclusiveBetween`/`ExclusiveBetween` → ambos límites; `Length`/`MinimumLength`/`MaximumLength` → `minLength`/`maxLength`; `NotEmpty` → `minLength: 1`/`minItems: 1`; `Matches` (patrón constante, sin `RegexOptions`) → `pattern`.
- **Política conservadora** (el documento nunca afirma una restricción que el servicio no aplique): se ignoran las reglas con `When`/`Unless`, las de otros rule sets, `RuleForEach`, rutas anidadas, `Must`/`Custom`/asíncronas y `Matches` con opciones. Nunca se infiere `required` de nivel superior: el validador ve el comando ya mapeado (con posibles valores por defecto, p. ej. `CreateGroupRequest.ConsolidationMethod = "FULL"`).
- **`.Must(...)` no es introspectable** (es un delegado cerrado): se añaden dos reglas legibles en `Nexus.BuildingBlocks.Application.Validation` — `MustBeOneOf(values)` → `enum` y `MustContainNonBlankKeys(keys)` → `required` de las claves del diccionario. Siguen la convención de FluentValidation (`null` pasa; se combina con `NotNull()`). Los dos helpers compartidos `MustHaveGermanCaption()` y `MustBeSupportedLanguage()` se reescribieron sobre ellas (mismos mensajes, misma aceptación/rechazo), sin tocar ningún validador individual.
- **`ResponseNumberOpenApiSchemaTransformer`**: quita la alternativa `string` y el patrón numérico solo en propiedades de tipos que no aparecen en ningún cuerpo de petición y cuyo `JsonNumberHandling` efectivo no incluye `WriteAsString`.

### Resultado (servicio real vs. `contracts/openapi/platform.yaml`)

| Momento | Resumen de `oasdiff breaking` |
|---|---|
| Antes (sin transformer, sin líneas nuevas en `oasdiff-err-ignore.txt`) | `261 changes: 214 error, 47 warning, 0 info` |
| Transformer + helpers compartidos (sin líneas nuevas) | `221 changes: 174 error, 47 warning, 0 info` |
| + 36 líneas de seguridad en `oasdiff-err-ignore.txt` | `185 changes: 138 error, 47 warning, 0 info` |

Cerradas por completo: `request-parameter-min-set` (16), `request-parameter-max-set` (8), `request-property-pattern-added` (8), `new-required-request-property` (4), `api-security-anonymous-access-removed` (36, por lista), y 2 de 6 `became-enum` (`preferredLanguage`). Las 47 `warning` (comparación de ramas `allOf` de oasdiff) no hacen fallar el job y no cambian.

### Lo que queda (138 `error`) y qué hace falta

1. ⚑ **130 `response-property-became-optional` + 2 `response-property-type-changed` (`format: int16`)** — contrato más laxo que el servicio en respuestas. Opciones: (a) Gate C: el contrato añade esas propiedades a `required` (siguen siendo `type: [X, "null"]`) y `format: int16` a `minorUnits` — refleja lo que el servicio ya hace y da tipos no opcionales al cliente Angular generado; (b) relajar `required` en los DTO de respuesta del servicio (cambio de forma real, peor); (c) listas de excepciones (descartado por este mismo ADR). Recomendación: (a). No se decide aquí.
2. **4 `became-enum` + 1 `pattern-added` — reglas `.Must(...)` opacas en validadores individuales**, fuera del alcance de este refinamiento. Una línea cada uno, comprobado en local (bajan a 0 con estos cambios): `CreateGroupValidator`/`UpdateGroupValidator` `ConsolidationMethod` → `.NotNull().MustBeOneOf(AllowedConsolidationMethods)`; `CreatePartyValidator` `PartyType` → `.NotNull().MustBeOneOf([...])`; `GetPartiesValidator` → `RuleFor(q => q.PartyType).MustBeOneOf([...])` sin el `When(... is not null)` (la regla ya deja pasar `null`); `UpdateCurrencyValidator` `Code` → `.Matches("^[A-Z]{3}$")`.
3. **1 `request-parameter-pattern-added` en `GET /v1/currencies/{code}`** — no es un hueco de proyección: `GetCurrencyQuery` no tiene validador, así que el servicio de verdad no aplica el patrón del contrato (un código en minúsculas da 404, no 400). Requiere decidir en el track B si se añade `GetCurrencyValidator`.

### Observación sobre la dirección del job (no se decide aquí)

Con `base = servicio` y `revision = contrato`, oasdiff marca exactamente los casos en que el servicio es *más permisivo* que el contrato en peticiones y *más estricto* en respuestas — es decir, documento generado impreciso, no incumplimiento. Un incumplimiento real en la dirección contraria (el servicio rechaza una petición que el contrato permite, o devuelve algo que el contrato no admite) no produce ningún `error`. Lo que hace este transformer (que el documento generado diga la verdad) hace que los hallazgos restantes sean reales, pero no cubre ese otro sentido.
