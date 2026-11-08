# syntax=docker/dockerfile:1
#
# nexus-projections (conventions-events.md §8): Kafka -> ClickHouse projections. Build context =
# repository root:
#   docker build -f src/Projections/Dockerfile -t nexus-projections:local .
# (compose: service nexus-projections, profile "apps"). The context is filtered by
# src/Projections/Dockerfile.dockerignore (BuildKit reads <Dockerfile>.dockerignore).
#
# TFM net10.0 (src/Directory.Build.props): SDK 10.0 to build, ASP.NET Core 10.0 runtime to run
# (the worker uses the ASP.NET Core shared framework for Microsoft.Extensions.Hosting and /health).
#
# Entrypoint: `dotnet nexus-projections.dll` with the CLI arguments appended, so
#   docker compose run --rm nexus-projections rebuild event_log
# runs the rebuild (make projections-rebuild P=event_log). No argument = the worker.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# 1. Restore only (cached until a project file or a package version changes). Central package
#    management: every version lives in src/Directory.Packages.props. The ProjectReference chain of
#    Nexus.Projections is Messaging -> Persistence -> Application/Domain.
COPY global.json .editorconfig ./
COPY src/Directory.Build.props src/Directory.Packages.props src/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Domain/Nexus.BuildingBlocks.Domain.csproj src/BuildingBlocks/Nexus.BuildingBlocks.Domain/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Application/Nexus.BuildingBlocks.Application.csproj src/BuildingBlocks/Nexus.BuildingBlocks.Application/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/Nexus.BuildingBlocks.Persistence.csproj src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Messaging/Nexus.BuildingBlocks.Messaging.csproj src/BuildingBlocks/Nexus.BuildingBlocks.Messaging/
COPY src/Projections/Nexus.Projections/Nexus.Projections.csproj src/Projections/Nexus.Projections/
RUN dotnet restore src/Projections/Nexus.Projections/Nexus.Projections.csproj

# 2. Sources, the ClickHouse migrations (db/clickhouse/V*.sql, applied at startup) and the topic
#    registry (contracts/events/topics.yaml, pii flags EV-R5). The csproj copies both next to the
#    binaries (db/clickhouse/, contracts/events/); the publish step below checks they arrived.
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Domain/ src/BuildingBlocks/Nexus.BuildingBlocks.Domain/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Application/ src/BuildingBlocks/Nexus.BuildingBlocks.Application/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/ src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Messaging/ src/BuildingBlocks/Nexus.BuildingBlocks.Messaging/
COPY src/Projections/Nexus.Projections/ src/Projections/Nexus.Projections/
COPY db/clickhouse/ db/clickhouse/
COPY contracts/events/topics.yaml contracts/events/topics.yaml
RUN dotnet publish src/Projections/Nexus.Projections/Nexus.Projections.csproj \
      --configuration Release --no-restore --output /app/publish \
    && test -n "$(find /app/publish/db/clickhouse -name 'V*.sql' -print -quit)" \
    && test -f /app/publish/contracts/events/topics.yaml

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# No path overrides: ProjectionOptions.MigrationsDirectory and TopicRegistry.DefaultPath both default to
# <binaries>/db/clickhouse and <binaries>/contracts/events/topics.yaml, where the publish step puts them.
ENV DOTNET_ENVIRONMENT=Production

# Non-root user provided by the .NET images.
USER $APP_UID

ENTRYPOINT ["dotnet", "nexus-projections.dll"]
