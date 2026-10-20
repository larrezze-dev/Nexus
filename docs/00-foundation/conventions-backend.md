# Convenciones de backend (.NET) `[+]`

Base: ADR-0007 (librerías). Todo lo de este documento es convención de implementación nueva.

## §1 Plataforma

| Tema | Elección |
|---|---|
| Runtime | .NET 10 (LTS), `net10.0` |
| API | ASP.NET Core **Minimal APIs**, agrupadas por recurso (`MapGroup`) |
| OpenAPI | `Microsoft.AspNetCore.OpenApi`. El documento expuesto se compara con `contracts/openapi/<servicio>.yaml` en CI |
| Escritura | EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL`, convención `snake_case` (`EFCore.NamingConventions`). **Sin migraciones EF** |
| Lectura | Dapper sobre la misma conexión transaccional |
| Validación | FluentValidation |
| Mediación | Dispatcher propio en `BuildingBlocks.Application` (sin MediatR) |
| Tiempo | `TimeProvider` inyectado. Nunca `DateTime.Now`/`UtcNow` directo |
| IDs | `Guid.CreateVersion7(timeProvider.GetUtcNow())` |

## §2 Capas y dependencias

```
Api → Application → Domain
 ↓         ↓
Infrastructure → Application, Domain
```

- `Domain` no depende de nada salvo `BuildingBlocks.Domain`. Sin EF, sin Kafka, sin ASP.NET.
- `Application` define interfaces (`I<Agg>Repository`, `IReadDb`) que implementa `Infrastructure`.
- `Api` solo traduce HTTP ↔ comandos/consultas. Ninguna regla de negocio en endpoints.
- Una prueba de arquitectura (NetArchTest o reflexión propia) en cada `*.Tests` verifica estas dependencias.

## §3 Dominio

- **Agregados** con constructor privado y métodos de fábrica (`AssetType.Create(...)`) que devuelven `Result<T>`.
- Las invariantes viven en el agregado. Cada invariante cita su regla: `// Rule CAT-R2`.
- Los agregados **acumulan eventos de dominio** (`RaiseEvent(new AssetTypeCreated(...))`). No los publican.
- Value objects para conceptos con reglas: `Money`, `Quantity` (valor + `uom_id`), `Code`, `I18nText`, `DateRange`.
- Errores de negocio esperados → `Result.Failure(Error)` con `Error.Code` = id de regla (`"CAT-R2"`). Las excepciones quedan para fallos técnicos y bugs.

## §4 Persistencia y transacción

Cada petición se ejecuta en **una** transacción gestionada por `IUnitOfWork` (`BuildingBlocks.Persistence`):

1. Abre conexión desde `NpgsqlDataSource` (rol `svc_<servicio>`).
2. `BEGIN`.
3. `SET LOCAL app.current_user_id`, `SET LOCAL app.correlation_id` (`conventions-database.md §7`).
4. El handler trabaja con el `DbContext` (escritura) y/o Dapper (lectura), ambos sobre esa conexión y transacción.
5. Al confirmar: recoge los eventos de dominio de los agregados rastreados, los serializa a Avro y los inserta en `outbox.message` (`conventions-events.md §3`).
6. `COMMIT`.

Reglas:
- **BE-R1:** toda consulta, incluidas las de lectura, pasa por el `UnitOfWork`. Una conexión sin `app.current_user_id` no ve filas multi-Mandant (las funciones devuelven vacío), y un test de humo lo comprueba.
- **BE-R2:** prohibido `DbContext.SaveChanges` fuera del `UnitOfWork`.
- **BE-R3:** concurrencia optimista con `xmin` (`UseXminAsConcurrencyToken`). Un conflicto devuelve 409.
- **BE-R4:** las tablas append-only se mapean como entidades sin setters públicos; el repositorio solo expone `Add`.

## §5 Autorización (RBAC, A6)

Cada endpoint declara su permiso:

```csharp
group.MapPost("/", CreateAssetType.Handle)
     .RequirePermission("ASSET_TYPE", PermissionAction.Create);
```

- El filtro llama a `access.has_permission(resource, action, legal_entity_id, warehouse_id, project_id)` dentro de la transacción de la petición (`domain/access.md §4`). El alcance se toma del cuerpo o de la ruta según indique el endpoint.
- Para listados, el permiso se comprueba sin alcance (`has_permission('ASSET_TYPE','READ')` = ¿tiene el permiso en algún alcance?) y RLS filtra las filas.
- Sin caché de permisos entre peticiones. Una revocación aplica en la siguiente petición.
- Mismo nombre de permiso en el OpenAPI: extensión `x-permission: ASSET_TYPE:CREATE` (`conventions-api.md §3`).

## §6 Comandos y consultas

```csharp
public sealed record CreateAssetTypeCommand(...) : ICommand<Guid>;
internal sealed class CreateAssetTypeHandler : ICommandHandler<CreateAssetTypeCommand, Guid> { ... }
internal sealed class CreateAssetTypeValidator : AbstractValidator<CreateAssetTypeCommand> { ... }

public sealed record GetAssetTypesQuery(...) : IQuery<Page<AssetTypeListItem>>;
```

- Un comando modifica como máximo **un** agregado. Si un caso de uso afecta a dos, el segundo reacciona al evento del primero.
- Validación de forma (requeridos, longitudes, formatos) en el validador → 400. Reglas de negocio en el dominio → 422.
- Las consultas devuelven *read models* planos, nunca entidades de dominio.

## §7 Errores HTTP

Todas las respuestas de error son `application/problem+json` (RFC 9457):

| Situación | Estado | `type` / `code` |
|---|---|---|
| Validación de forma | 400 | `validation`, con `errors` por campo |
| Sin token / token inválido | 401 | — |
| Sin permiso | 403 | `forbidden`, con el permiso exigido |
| No existe o RLS lo oculta | 404 | `not_found` (nunca se distingue entre ambos) |
| Conflicto de concurrencia | 409 | `concurrency` |
| Transición de estado no permitida | 409 | `invalid_state`, con estado actual y destino |
| Regla de negocio | 422 | `business_rule`, `code` = id de regla (`STK-R2`) |

## §8 Observabilidad

- OpenTelemetry (trazas, métricas, logs) → OTLP. Local: Aspire Dashboard o Jaeger en compose; Azure: Azure Monitor.
- Cabecera `X-Correlation-Id`: la crea el gateway si no viene, se propaga a servicios, a `app.correlation_id` y a `correlation_id` del evento.
- Logs estructurados con `ILogger`. Nunca se registran valores de campos PII (`domain/gdpr.md`).

## §9 Configuración

- `appsettings.json` + variables de entorno (`NEXUS__Section__Key`).
- Secretos: `dotnet user-secrets` en local; Azure Key Vault en Azure (0.11).
- Cadenas de conexión por servicio, con su rol `svc_<servicio>`. Ningún servicio usa `nexus_owner`.

## §10 Paquetes aprobados

| Uso | Paquete |
|---|---|
| PostgreSQL | `Npgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `Dapper` |
| Validación | `FluentValidation`, `FluentValidation.DependencyInjectionExtensions` |
| Kafka | `Confluent.Kafka`, `Confluent.SchemaRegistry`, `Confluent.SchemaRegistry.Serdes.Avro`, `Apache.Avro` |
| ClickHouse | `ClickHouse.Client` |
| Gateway | `Yarp.ReverseProxy` |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` (ADR-0006) |
| Blob | `Azure.Storage.Blobs` |
| Resiliencia | `Microsoft.Extensions.Http.Resilience` |
| Telemetría | `OpenTelemetry.Extensions.Hosting`, instrumentaciones ASP.NET Core / HttpClient / Npgsql, exportador OTLP |
| Tests | `xunit.v3`, `Shouldly`, `NSubstitute`, `Testcontainers.PostgreSql`, `Testcontainers.Kafka`, `Testcontainers.ClickHouse`, `Bogus` |

Cualquier paquete fuera de esta lista requiere ADR (H13).
