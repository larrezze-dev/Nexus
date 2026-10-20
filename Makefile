.PHONY: up down reset doctor db-migrate seed-dev contracts mock-api build test test-rls test-e2e lint rule-coverage

up:
	@echo "pending: paso 0.2"

down:
	@echo "pending: paso 0.2"

reset:
	@echo "pending: paso 0.2"

doctor:
	@echo "=== Environment Check ==="
	@echo "dotnet:"; dotnet --version 2>/dev/null || echo "  not found"
	@echo "node:"; node --version 2>/dev/null || echo "  not found"
	@echo "npm:"; npm --version 2>/dev/null || echo "  not found"
	@echo "docker:"; docker --version 2>/dev/null || echo "  not found"
	@echo "make:"; make --version 2>/dev/null | head -1 || echo "  not found"

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
	@if [ -d "contracts/openapi" ]; then \
		npx -y @stoplight/spectral-cli lint "contracts/openapi/*.yaml" --ruleset contracts/openapi/.spectral.yaml 2>/dev/null || true; \
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
	@dotnet test src/Nexus.sln
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
