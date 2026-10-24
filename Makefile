.PHONY: up down reset logs ps doctor db-migrate db-info db-validate seed-dev contracts mock-api build test test-rls test-e2e lint rule-coverage

COMPOSE_FILE  := infra/compose/docker-compose.yml
COMPOSE_ENV   := infra/compose/.env
COMPOSE_ENV_EXAMPLE := infra/compose/.env.example
COMPOSE       := docker compose -f $(COMPOSE_FILE)
COMPOSE_INFRA := docker compose -f $(COMPOSE_FILE) --env-file $(COMPOSE_ENV) --profile infra
HEALTH_TIMEOUT := 180

up:
	@test -f $(COMPOSE_ENV) || { test -f $(COMPOSE_ENV_EXAMPLE) || { echo "ERROR: $(COMPOSE_ENV_EXAMPLE) not found"; exit 1; }; cp $(COMPOSE_ENV_EXAMPLE) $(COMPOSE_ENV) && echo "created $(COMPOSE_ENV) from $(COMPOSE_ENV_EXAMPLE)"; }
	@if $(COMPOSE) up --help 2>/dev/null | grep -q -- '--wait'; then \
		$(COMPOSE_INFRA) up -d --wait || { echo "ERROR: infra did not become healthy (see 'make ps' / 'make logs')"; exit 1; }; \
		echo "all healthy"; \
	else \
		echo "docker compose has no --wait flag; polling health (timeout $(HEALTH_TIMEOUT)s)"; \
		$(COMPOSE_INFRA) up -d || exit 1; \
		deadline=$$(( $$(date +%s) + $(HEALTH_TIMEOUT) )); \
		while :; do \
			ids=$$($(COMPOSE_INFRA) ps -q); \
			st=$$(for id in $$ids; do docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $$id; done); \
			if echo "$$st" | grep -q unhealthy; then echo "ERROR: unhealthy service(s) (see 'make ps' / 'make logs')"; exit 1; fi; \
			if [ -n "$$ids" ] && ! echo "$$st" | grep -qvE '^(healthy|running)$$'; then echo "all healthy"; break; fi; \
			if [ $$(date +%s) -ge $$deadline ]; then echo "ERROR: timeout ($(HEALTH_TIMEOUT)s) waiting for healthy services (see 'make ps' / 'make logs')"; exit 1; fi; \
			sleep 3; \
		done; \
	fi

down:
	@$(COMPOSE) --profile infra down

reset:
	@$(COMPOSE) --profile infra down -v

logs:
	@$(COMPOSE) --profile infra logs -f $(S)

ps:
	@$(COMPOSE) --profile infra ps

doctor:
	@echo "=== Environment Check ==="
	@echo "dotnet:"; dotnet --version 2>/dev/null || echo "  not found"
	@echo "node:"; node --version 2>/dev/null || echo "  not found"
	@echo "npm:"; npm --version 2>/dev/null || echo "  not found"
	@echo "docker:"; docker --version 2>/dev/null || echo "  not found"
	@echo "make:"; make --version 2>/dev/null | head -1 || echo "  not found"
	@if [ -f tools/doctor.sh ]; then \
		bash tools/doctor.sh; \
	else \
		echo "warning: tools/doctor.sh not found yet (paso 0.2 en curso) - skipping"; \
	fi

FLYWAY_IMAGE := flyway/flyway:13.11.1-alpine
FLYWAY_URL   := jdbc:postgresql://postgres:5432/nexus

# Runs Flyway inside the `nexus` compose network (host `postgres`, role nexus_migrator).
# The password is read from $(COMPOSE_ENV) on the host and handed to the container via
# `-e FLYWAY_PASSWORD` (name only, value taken from the environment), so it never shows
# up in argv / `ps`. --env-file is not used: it only feeds the container, not this shell.
# Ownership: db/migrations/afterConnect.sql is a Flyway callback (not a migration)
# that runs `SET ROLE nexus_owner` after every connection, so objects created by
# migrations are owned by nexus_owner rather than nexus_migrator -- see that file.
define flyway_run
	@set -a; . ./$(COMPOSE_ENV); set +a; \
	export FLYWAY_PASSWORD="$$NEXUS_MIGRATOR_PASSWORD"; \
	docker run --rm --network nexus \
		-v "$(CURDIR)/db:/flyway/sql:ro" \
		-e FLYWAY_PASSWORD \
		$(FLYWAY_IMAGE) \
		-url=$(FLYWAY_URL) \
		-user=nexus_migrator \
		-locations=filesystem:/flyway/sql/migrations \
		$(1)
endef

db-migrate:
	$(call flyway_run,migrate)

db-info:
	$(call flyway_run,info)

db-validate:
	$(call flyway_run,validate)

# Seeds (common + dev) live under db/seed/. They are data, not schema, so they keep their
# own Flyway history table (flyway_seed_history) and never mix with flyway_schema_history.
# Same credential handling as flyway_run (see above): password only via -e FLYWAY_PASSWORD.
seed-dev:
	@set -a; . ./$(COMPOSE_ENV); set +a; \
	export FLYWAY_PASSWORD="$$NEXUS_MIGRATOR_PASSWORD"; \
	docker run --rm --network nexus \
		-v "$(CURDIR)/db:/flyway/sql:ro" \
		-e FLYWAY_PASSWORD \
		$(FLYWAY_IMAGE) \
		-url=$(FLYWAY_URL) \
		-user=nexus_migrator \
		-locations=filesystem:/flyway/sql/seed/common,filesystem:/flyway/sql/seed/dev \
		-table=flyway_seed_history \
		migrate

test-rls:
	@echo "pending: paso 0.3"

# Prism mock of contracts/openapi/<S>.yaml on the fixed port 4010. Only one mock at a time
# for now; the "4010 + n" scheme for several services in parallel is deferred to a later step.
mock-api:
	@if [ -z "$(S)" ]; then echo "ERROR: usage: make mock-api S=<servicio>"; exit 1; fi
	@file="contracts/openapi/$(S).yaml"; \
	if [ ! -f "$$file" ]; then echo "ERROR: $$file not found"; exit 1; fi; \
	echo "Mocking $$file on http://localhost:4010 (Prism) ..."; \
	npx -y @stoplight/prism-cli mock "$$file" -p 4010

test-e2e:
	@echo "pending: paso 1.3"

contracts:
	@files=$$(find contracts/openapi -maxdepth 1 -type f -name '*.yaml' ! -name '.*' 2>/dev/null); \
	if [ -n "$$files" ]; then \
		npx -y @stoplight/spectral-cli lint $$files --ruleset contracts/openapi/.spectral.yaml; \
	else \
		echo "No service OpenAPI files yet (contracts/openapi/*.yaml) - nothing to lint."; \
	fi
	@if [ -f "tools/check-permissions.sh" ]; then \
		bash tools/check-permissions.sh; \
	fi

build:
	@dotnet build src/Nexus.sln
	@if [ -f "web/package.json" ]; then \
		npm --prefix web ci && npm --prefix web run build; \
	fi

test:
	@dotnet test --solution src/Nexus.sln
	@if [ -f "web/package.json" ]; then \
		npm --prefix web test; \
	fi

lint:
	@if [ -f "web/package.json" ]; then \
		npm --prefix web run lint; \
	fi

rule-coverage:
	@if [ -f "tools/rule-coverage.sh" ]; then \
		bash tools/rule-coverage.sh STEP=$(STEP); \
	else \
		echo "tools/rule-coverage.sh not found"; \
	fi
