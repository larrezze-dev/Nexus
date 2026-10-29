# ADR-0014 · Conformidad OpenAPI del servicio vs. reglas de FluentValidation

- **Estado:** Propuesto ⚑ abierto — bloquea el cierre de 0.4 track B5 (job `conformance`)
- **Fecha:** 2026-10-29
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

## Decisión propuesta (a confirmar)

Construir un transformador de OpenAPI (`IOpenApiOperationTransformer`/`IOpenApiSchemaTransformer`, mismo mecanismo que ya usa `PermissionOpenApiOperationTransformer` en `Nexus.BuildingBlocks.Web/OpenApi/`) que inspeccione los `AbstractValidator<T>` de FluentValidation registrados para cada tipo de petición/respuesta y proyecte sus reglas al esquema generado: `RuleFor(...).GreaterThanOrEqualTo`/`InclusiveBetween` → `minimum`/`maximum`; `.Must(value => value is "A" or "B")` sobre una lista fija → `enum`; una regla de "no vacío"/presencia de una clave fija (como `MustHaveGermanCaption`) → `required`; patrones de regex ya usados en `.Matches(...)` → `pattern`. Esto cierra el gap de raíz, una vez, en el building block compartido — en vez de en cada servicio o en una lista de excepciones que crece sin límite.

## Alternativas descartadas (a valorar)

| Alternativa | Por qué no (o por qué sí, a decidir) |
|---|---|
| **Seguir ampliando `tools/oasdiff-err-ignore.txt`** | Escala mal (214 líneas ya, crecería con cada paso) y el propio fichero documenta por qué eso no es su propósito: dejaría de ser una lista de excepciones *examinadas* y pasaría a ser una supresión en bloque. |
| **Debilitar el job `conformance`** (bajar de severidad las categorías `request-parameter-min-set`, `response-property-became-optional`, etc., con `--severity-levels`) | Más simple de implementar ya, pero pierde la capacidad de detectar una regresión *real* de forma en esas mismas categorías (p. ej. alguien quita un `required` real del contrato por error) — el job dejaría de proteger justo lo que más cambia paso a paso. |
| **No generar el documento con reflexión; mantener `platform.yaml` como la única fuente servida** (el servicio sirve el YAML del contrato directamente en `/openapi/v1.json`, en vez de generarlo) | Elimina el gap de raíz (ya no hay dos documentos que puedan discrepar), pero cambia el propósito del job — ya no verificaría que el servicio *implementa* lo que el documento describe, solo que el fichero existe. Contradice la idea original del job (detectar que el servicio implementado se desvía del contrato). |

## Consecuencias

- Con la decisión propuesta: un único punto de mantenimiento (el transformer) en vez de una lista de excepciones por endpoint; cada paso futuro con FluentValidation se beneficia automáticamente, sin trabajo adicional en su propio track B.
- Coste: trabajo nivel 3 (nx-architect) en un building block compartido, antes de que 0.4 track B pueda cerrar su job `conformance` en verde sin más líneas de ignore-file.
- Mientras este ADR esté en `Propuesto`, 0.4 track B5 no añade las 214 líneas al ignore-file ni debilita el job — se documenta aquí como hallazgo abierto y el cierre de B5 (PR, `Gate`) queda a la espera de esta decisión.

## Cómo se verifica

Con la decisión tomada: `dotnet run` real del servicio + `oasdiff breaking` contra `contracts/openapi/platform.yaml` (igual que este ADR lo reprodujo) vuelve a 0 hallazgos `error` nuevos (solo el ya aceptado de `/v1/me`, o ninguno si el transformer también resuelve seguridad).
