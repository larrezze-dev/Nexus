# Estructura del monorepo `[+]`

Un único repositorio git. Los nombres de carpetas y proyectos son fijos: el agente no crea carpetas de primer nivel nuevas sin ADR.

## §1 Árbol de primer nivel

```
nexus/
├── CLAUDE.md
├── Makefile                      # targets: up, down, db-migrate, build, test, contracts, mock-api
├── .editorconfig
├── .gitignore
├── .github/workflows/            # CI (ver §7)
├── docs/                         # especificación ejecutable
├── contracts/
│   ├── openapi/<servicio>.yaml   # platform, catalog, operations, commercial, resources, controlling, field, solver
│   └── events/<contexto>/<aggregate>.<evento>.v<n>.avsc
├── db/
│   ├── migrations/               # Flyway (ver §4)
│   └── seed/{common,dev,test}/
├── src/                          # .NET
├── solver/                       # Kotlin / Spring Boot / Timefold (Gradle)
├── web/                          # Angular
├── mobile/                       # Android / Kotlin (Gradle)
├── infra/
│   ├── compose/                  # docker-compose.yml + configuración local
│   ├── terraform/                # módulos + entornos
│   └── arc/                      # manifiestos Azure Arc (edge)
└── tests/
    ├── contract/                 # oasdiff, validación Avro
    └── e2e/                      # Playwright
```

Windows: el entorno local se ejecuta en WSL2 (Docker Desktop ya lo requiere). `make` se usa desde WSL2.

## §2 `src/` — .NET

```
src/
├── Nexus.sln
├── Directory.Build.props         # net10.0, Nullable enable, TreatWarningsAsErrors, analizadores
├── Directory.Packages.props      # gestión central de versiones NuGet
├── BuildingBlocks/
│   ├── Nexus.BuildingBlocks.Domain/        # Entity, AggregateRoot, DomainEvent, Result, Error, Guard
│   ├── Nexus.BuildingBlocks.Application/   # ICommand/IQuery, handlers, dispatcher, validación, IClock, ICurrentUser
│   ├── Nexus.BuildingBlocks.Persistence/   # NpgsqlDataSource, UnitOfWork con sesión RLS, OutboxWriter, convenciones EF
│   ├── Nexus.BuildingBlocks.Messaging/     # consumidor Kafka base, Inbox, serdes Avro, DLQ, reintentos
│   ├── Nexus.BuildingBlocks.Web/           # auth JWT, PermissionFilter, ProblemDetails, OpenAPI, paginación
│   ├── Nexus.BuildingBlocks.Analytics/     # cliente ClickHouse + inyección de filtro de alcance
│   └── Nexus.BuildingBlocks.Testing/       # fixtures Testcontainers, helpers RLS, builders
├── Contracts/
│   └── Nexus.Contracts.Events/             # clases generadas desde contracts/events (no se editan a mano)
├── Gateway/Nexus.Gateway/
├── Platform/                               # un bloque igual por servicio de negocio:
│   ├── Nexus.Platform.Api/                 #   host: endpoints, DI, configuración
│   ├── Nexus.Platform.Application/         #   comandos, consultas, handlers, validadores
│   ├── Nexus.Platform.Domain/              #   agregados, value objects, reglas
│   ├── Nexus.Platform.Infrastructure/      #   EF Core (escritura), Dapper (lectura), consumidores
│   └── Nexus.Platform.Tests/               #   unit + integración
├── Catalog/ …  Operations/ …  Commercial/ …  Resources/ …  Controlling/ …  Field/ …
├── OutboxRelay/Nexus.OutboxRelay/
├── Projections/Nexus.Projections/          # un consumidor por proyección, en carpetas por contexto
└── Migration/Nexus.Migration/              # herramienta ETL (Fase 7)
```

Dentro de cada servicio, el código se organiza **por contexto y por caso de uso**, no por tipo técnico:

```
Nexus.Catalog.Application/
├── AssetTypes/
│   ├── CreateAssetType/        # Command, Handler, Validator
│   ├── ChangeTrackingMode/
│   └── GetAssetTypes/          # Query, Handler, ReadModel
└── UnitsOfMeasure/ …
```

## §3 Contratos

| Ruta | Formato | Genera |
|---|---|---|
| `contracts/openapi/<servicio>.yaml` | OpenAPI 3.1 | Cliente Angular (`web/src/app/api/<servicio>/`), mock Prism, test de conformidad del backend |
| `contracts/events/<contexto>/*.avsc` | Avro | `Nexus.Contracts.Events` (C#), clases Kotlin del Solver, registro en Schema Registry |

El código generado **no se versiona**: se regenera con `make contracts` y en CI.

## §4 Migraciones (Flyway)

| Tipo | Nombre | Contenido |
|---|---|---|
| Versionada | `V<YYYYMMDDHHMM>__<contexto>__<descripcion>.sql` | Tablas, columnas, índices, constraints, grants |
| Repetible | `R__<contexto>__<objeto>.sql` | Funciones, vistas, políticas RLS (idempotentes con `CREATE OR REPLACE` / `DROP POLICY IF EXISTS`) |

- La versión con marca de tiempo evita colisiones entre ramas paralelas.
- Una migración versionada ya aplicada en `main` **nunca** se edita. Se corrige con otra nueva.
- Ejemplo: `V202610201015__catalog__asset_type.sql`, `R__access__scope_functions.sql`.

## §5 `web/` — Angular

```
web/
├── angular.json
├── src/app/
│   ├── core/            # auth (OIDC), interceptores, layout, menú, i18n, manejo de errores
│   ├── shared/
│   │   ├── ui/          # data-grid, form-field, i18n-caption-editor, status-badge, confirm-dialog
│   │   └── platform/    # approval-panel, document-panel, audit-trail, version-history
│   ├── features/<contexto>/   # rutas, páginas, componentes y facade por contexto
│   └── api/<servicio>/        # generado, no se edita
├── src/assets/i18n/{de,en,fr,it,es}.json
└── e2e → tests/e2e
```

## §6 `solver/` y `mobile/`

```
solver/                              mobile/
├── build.gradle.kts                 ├── build.gradle.kts
├── src/main/kotlin/ch/nussli/nexus/ ├── app/src/main/kotlin/ch/nussli/nexus/mobile/
│   └── solver/                      │   ├── core/      (auth, sync, db local, rfid)
│       ├── api/      (REST)         │   ├── warehouse/
│       ├── domain/   (Timefold)     │   └── field/
│       ├── input/    (CH + PG)      └── …
│       └── output/   (Kafka)
└── src/test/kotlin/…
```

## §7 CI (GitHub Actions) `[+]`

| Job | Qué hace | Bloquea merge |
|---|---|---|
| `contracts` | Lint OpenAPI (Spectral), validación Avro, compatibilidad backward contra Schema Registry local | Sí |
| `db` | Flyway `migrate` sobre PostgreSQL limpio + `validate` | Sí |
| `dotnet` | build + tests (Testcontainers) | Sí |
| `conformance` | Compara el OpenAPI que expone cada servicio con su contrato (oasdiff, sin breaking changes) | Sí |
| `web` | lint + tests + build | Sí |
| `jvm` | Gradle build + tests (`solver/`, `mobile/`) | Sí, cuando existan |
| `e2e` | Playwright contra docker-compose | Desde Fase 1, no bloqueante hasta 1.3 |

Si el repositorio remoto no es GitHub (p. ej. Azure DevOps), el paso 0.1 traduce los jobs 1:1 y lo registra en un ADR.
