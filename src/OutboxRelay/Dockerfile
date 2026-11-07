# syntax=docker/dockerfile:1
#
# nexus-relay (conventions-events.md §4). Build context = repository root:
#   docker build -f src/OutboxRelay/Dockerfile -t nexus-relay:local .
# (compose: service nexus-relay, profile "apps"). The context is filtered by
# src/OutboxRelay/Dockerfile.dockerignore (BuildKit reads <Dockerfile>.dockerignore).
#
# TFM net10.0 (src/Directory.Build.props): SDK 10.0 to build, ASP.NET Core 10.0 runtime to run
# (the worker uses the ASP.NET Core shared framework for Microsoft.Extensions.Hosting).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# 1. Restore only (cached until a project file or a package version changes). Central package
#    management: every version lives in src/Directory.Packages.props.
COPY global.json .editorconfig ./
COPY src/Directory.Build.props src/Directory.Packages.props src/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Domain/Nexus.BuildingBlocks.Domain.csproj src/BuildingBlocks/Nexus.BuildingBlocks.Domain/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Application/Nexus.BuildingBlocks.Application.csproj src/BuildingBlocks/Nexus.BuildingBlocks.Application/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/Nexus.BuildingBlocks.Persistence.csproj src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/
COPY src/OutboxRelay/Nexus.OutboxRelay/Nexus.OutboxRelay.csproj src/OutboxRelay/Nexus.OutboxRelay/
RUN dotnet restore src/OutboxRelay/Nexus.OutboxRelay/Nexus.OutboxRelay.csproj

# 2. Sources and the Avro contracts (ADR-0018: the relay encodes each row with the local .avsc its
#    writer validated). The project copies contracts/events/<context>/*.avsc (not _test) into the
#    publish output under contracts/events/.
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Domain/ src/BuildingBlocks/Nexus.BuildingBlocks.Domain/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Application/ src/BuildingBlocks/Nexus.BuildingBlocks.Application/
COPY src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/ src/BuildingBlocks/Nexus.BuildingBlocks.Persistence/
COPY src/OutboxRelay/Nexus.OutboxRelay/ src/OutboxRelay/Nexus.OutboxRelay/
COPY contracts/events/ contracts/events/
RUN dotnet publish src/OutboxRelay/Nexus.OutboxRelay/Nexus.OutboxRelay.csproj \
      --configuration Release --no-restore --output /app/publish \
    && test -n "$(find /app/publish/contracts/events -name '*.avsc' -print -quit)" \
    && test ! -e /app/publish/contracts/events/_test

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Explicit, so the path does not depend on the binaries' location (RelayOptions.ContractsDirectory).
ENV NEXUS__RELAY__CONTRACTSDIRECTORY=/app/contracts/events \
    DOTNET_ENVIRONMENT=Production

# Non-root user provided by the .NET images.
USER $APP_UID

ENTRYPOINT ["dotnet", "Nexus.OutboxRelay.dll"]
