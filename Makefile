.PHONY: up down reset logs ps doctor db-migrate seed-dev contracts mock-api build test test-rls test-e2e lint rule-coverage

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
	else \
		echo "docker compose has no --wait flag; polling health (timeout $(HEALTH_TIMEOUT)s)"; \
		$(COMPOSE_INFRA) up -d || exit 1; \
		deadline=$$(( $$(date +%s) + $(HEALTH_TIMEOUT) )); \
		while :; do \
			ids=$$($(COMPOSE_INFRA) ps -q); \
			st=$$(for id in $$ids; do docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $$id; done); \
			if echo "$$st" | grep -q unhealthy; then echo "ERROR: unhealthy service(s) (see 'make ps' / 'make logs')"; exit 1; fi; \
			if [ -n "$$ids" ] && ! echo "$$st" | grep -qvE '^(healthy|running)$$'; then echo "infra is up and healthy"; break; fi; \
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

db-migrate:
	@echo "pending: paso 0.3"

seed-dev:
	@echo "pending: paso 0.3"

test-rls:
	@echo "pending: paso 0.3"

mock-api:
	@echo "pending: paso 0.5"

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
