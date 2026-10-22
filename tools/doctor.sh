#!/usr/bin/env bash
# doctor.sh — Verifies that the local NEXUS development stack (docker compose)
# is up and every service answers as expected: Docker itself, free/owned
# ports, container health, Postgres, Kafka, Schema Registry, ClickHouse,
# Azurite and a Keycloak password-grant token for the seeded user `usera`.
#
# Usage: tools/doctor.sh   (run from anywhere, it anchors to the repo root)
#
# Prints OK/FALLA per check, a summary at the end, and exits 1 if anything
# failed (0 if everything is OK).
set -uo pipefail

cd "$(dirname "$0")/.."

COMPOSE_FILE="infra/compose/docker-compose.yml"
ENV_FILE="infra/compose/.env"
COMPOSE=(docker compose -f "$COMPOSE_FILE")

# Keycloak client assumed for the password (Direct Access Grants) flow used in
# check 9. If the Keycloak track activates a different public client for this
# purpose, override with: DOCTOR_KEYCLOAK_CLIENT_ID=<id> tools/doctor.sh
KEYCLOAK_CLIENT_ID="${DOCTOR_KEYCLOAK_CLIENT_ID:-nexus-web}"
KEYCLOAK_REALM="nexus"
KEYCLOAK_TOKEN_URL="http://localhost:8180/realms/${KEYCLOAK_REALM}/protocol/openid-connect/token"
EXPECTED_OID="00000000-0000-7000-8000-00000000a002"
EXPECTED_AUD="api://nexus"

FAILED=0

ok() {
  echo "OK: $1"
}

fail() {
  echo "FALLA: $1" >&2
  FAILED=1
}

section() {
  echo
  echo "== $1 =="
}

# ---------------------------------------------------------------------------
# 1. Docker
# ---------------------------------------------------------------------------
section "1. Docker"
if docker info >/dev/null 2>&1; then
  ok "Docker daemon responde (docker info)."
else
  fail "Docker daemon no responde (docker info). Arranca Docker Desktop / el daemon y vuelve a intentarlo."
  echo
  echo "RESUMEN: 1 comprobación en FALLA (Docker no disponible), el resto no se ha ejecutado."
  exit 1
fi

if [[ ! -f "$COMPOSE_FILE" ]]; then
  fail "no existe $COMPOSE_FILE — no se puede continuar sin el fichero de compose."
  echo
  echo "RESUMEN: 1 comprobación en FALLA ($COMPOSE_FILE no encontrado), el resto no se ha ejecutado."
  exit 1
fi

# ---------------------------------------------------------------------------
# 2. Free ports (only checked if the stack is not already running)
# ---------------------------------------------------------------------------
section "2. Puertos"

PORTS=(
  "postgres:5432"
  "kafka:9092"
  "schema-registry:8081"
  "kafka-ui:8085"
  "clickhouse-http:8123"
  "clickhouse-native:9000"
  "azurite-blob:10000"
  "keycloak:8180"
  "otel-grpc:4317"
  "aspire-dashboard:18888"
)

port_in_use() {
  local port="$1"
  if command -v nc >/dev/null 2>&1; then
    nc -z -w1 localhost "$port" >/dev/null 2>&1
    return $?
  elif command -v lsof >/dev/null 2>&1; then
    lsof -i ":$port" >/dev/null 2>&1
    return $?
  else
    # No tool available to check: assume free and warn once (handled by caller).
    return 1
  fi
}

RUNNING_IDS="$("${COMPOSE[@]}" ps -q 2>/dev/null || true)"

if [[ -n "$RUNNING_IDS" ]]; then
  ok "el stack de '$COMPOSE_FILE' ya tiene contenedores corriendo — se omite la comprobación de puertos libres (se espera que estén ocupados por el propio compose)."
else
  if ! command -v nc >/dev/null 2>&1 && ! command -v lsof >/dev/null 2>&1; then
    fail "ni 'nc' ni 'lsof' están disponibles para comprobar puertos libres."
  else
    for entry in "${PORTS[@]}"; do
      name="${entry%%:*}"
      port="${entry##*:}"
      if port_in_use "$port"; then
        fail "puerto $port ($name) ya está ocupado y el stack no está arrancado — libéralo antes de 'make up'."
      else
        ok "puerto $port ($name) libre."
      fi
    done
  fi
fi

# ---------------------------------------------------------------------------
# 3. Container health
# ---------------------------------------------------------------------------
section "3. Salud de los contenedores"

# -a: also report stopped/exited containers — otherwise a service that
# crashed after being created simply doesn't appear and this check would
# pass by omission instead of catching it.
PS_JSON="$("${COMPOSE[@]}" ps -a --format json 2>/dev/null || true)"
EXPECTED_SERVICES="$("${COMPOSE[@]}" config --services 2>/dev/null || true)"

if [[ -z "$PS_JSON" ]]; then
  fail "'docker compose ps' no reporta contenedores corriendo. ¿Ejecutaste 'make up'?"
else
  HEALTH_REPORT="$(PS_JSON_DATA="$PS_JSON" EXPECTED_SERVICES_DATA="$EXPECTED_SERVICES" python3 - <<'PY'
import json
import os
import sys

# Read via env var, not stdin: a piped `producer | python3 - <<'PY'` has the
# heredoc win the fd0 redirect over the pipe, so sys.stdin.read() would always
# see an empty string here. Same reasoning applies to the Keycloak check below.
raw = os.environ["PS_JSON_DATA"]
expected = {s.strip() for s in os.environ["EXPECTED_SERVICES_DATA"].splitlines() if s.strip()}
containers = []
try:
    data = json.loads(raw)
    containers = data if isinstance(data, list) else [data]
except json.JSONDecodeError:
    # Older compose versions print one JSON object per line instead of an array.
    for line in raw.splitlines():
        line = line.strip()
        if line:
            containers.append(json.loads(line))

exit_code = 0
seen = set()
for c in containers:
    service = c.get("Service") or c.get("Name") or "?"
    seen.add(service)
    health = (c.get("Health") or "").strip().lower()
    state = (c.get("State") or "").strip().lower()
    if health:
        if health == "healthy":
            print(f"OK:{service}:health={health}")
        else:
            print(f"FAIL:{service}:health={health}")
            exit_code = 1
    else:
        if state == "running":
            print(f"OK:{service}:state={state} (sin healthcheck)")
        else:
            print(f"FAIL:{service}:state={state}")
            exit_code = 1

for missing in sorted(expected - seen):
    print(f"FAIL:{missing}:no existe ningún contenedor para este servicio")
    exit_code = 1

sys.exit(exit_code)
PY
)"
  HEALTH_RC=$?
  while IFS=: read -r status rest; do
    [[ -z "$status" ]] && continue
    if [[ "$status" == "OK" ]]; then
      ok "contenedor $rest"
    else
      fail "contenedor $rest"
    fi
  done <<< "$HEALTH_REPORT"
  if [[ $HEALTH_RC -ne 0 ]]; then
    fail "uno o más contenedores no están 'healthy' ni 'running'. Revisa 'docker compose -f $COMPOSE_FILE ps' y los logs."
  fi
fi

# ---------------------------------------------------------------------------
# 4. PostgreSQL
# ---------------------------------------------------------------------------
section "4. PostgreSQL"

POSTGRES_USER=""
if [[ -f "$ENV_FILE" ]]; then
  POSTGRES_USER="$(grep -E '^POSTGRES_USER=' "$ENV_FILE" | head -n1 | cut -d'=' -f2-)"
fi
if [[ -z "$POSTGRES_USER" ]]; then
  POSTGRES_USER="nexus"
  echo "INFO: POSTGRES_USER no encontrado en $ENV_FILE (o el fichero no existe aún) — usando valor por defecto '$POSTGRES_USER'."
fi

PSQL_OUT="$("${COMPOSE[@]}" exec -T postgres psql -U "$POSTGRES_USER" -d nexus -c "SELECT 1" 2>&1)"
PSQL_RC=$?
if [[ $PSQL_RC -eq 0 ]] && grep -q '1 row' <<< "$PSQL_OUT"; then
  ok "PostgreSQL responde a 'SELECT 1' con el usuario '$POSTGRES_USER' en la base 'nexus'."
else
  fail "PostgreSQL no respondió correctamente a 'SELECT 1'. Salida: ${PSQL_OUT}"
fi

# ---------------------------------------------------------------------------
# 5. Kafka
# ---------------------------------------------------------------------------
section "5. Kafka"

KAFKA_OUT="$("${COMPOSE[@]}" exec -T kafka /opt/kafka/bin/kafka-broker-api-versions.sh --bootstrap-server localhost:29092 2>&1)"
KAFKA_RC=$?
if [[ $KAFKA_RC -eq 0 ]] && grep -q '(id:' <<< "$KAFKA_OUT"; then
  ok "Kafka devuelve al menos un broker vía kafka-broker-api-versions.sh."
else
  fail "Kafka no devolvió brokers (o el script no está en /opt/kafka/bin/ en esta imagen). Salida: ${KAFKA_OUT}"
fi

# ---------------------------------------------------------------------------
# 6. Schema Registry
# ---------------------------------------------------------------------------
section "6. Schema Registry"

SR_OUT="$(curl -sf "http://localhost:8081/subjects" 2>&1)"
SR_RC=$?
if [[ $SR_RC -eq 0 ]] && python3 -c "import json,sys; d=json.loads(sys.argv[1]); sys.exit(0 if isinstance(d, list) else 1)" "$SR_OUT" 2>/dev/null; then
  ok "Schema Registry responde en /subjects con una lista JSON."
else
  fail "Schema Registry no respondió con una lista JSON en http://localhost:8081/subjects. Salida: ${SR_OUT}"
fi

# ---------------------------------------------------------------------------
# 7. ClickHouse
# ---------------------------------------------------------------------------
section "7. ClickHouse"

CLICKHOUSE_USER=""
CLICKHOUSE_PASSWORD=""
if [[ -f "$ENV_FILE" ]]; then
  CLICKHOUSE_USER="$(grep -E '^CLICKHOUSE_USER=' "$ENV_FILE" | head -n1 | cut -d'=' -f2-)"
  CLICKHOUSE_PASSWORD="$(grep -E '^CLICKHOUSE_PASSWORD=' "$ENV_FILE" | head -n1 | cut -d'=' -f2-)"
fi
CLICKHOUSE_USER="${CLICKHOUSE_USER:-nexus}"
if [[ -z "$CLICKHOUSE_PASSWORD" ]]; then
  echo "INFO: CLICKHOUSE_PASSWORD no encontrado en $ENV_FILE (o el fichero no existe aún) — probando sin contraseña."
fi

CH_OUT="$(curl -sf -u "${CLICKHOUSE_USER}:${CLICKHOUSE_PASSWORD}" "http://localhost:8123/?query=SELECT%201" 2>&1)"
CH_RC=$?
if [[ $CH_RC -eq 0 ]] && [[ "$(echo "$CH_OUT" | tr -d '[:space:]')" == "1" ]]; then
  ok "ClickHouse responde '1' a SELECT 1 vía HTTP (puerto 8123, usuario '$CLICKHOUSE_USER')."
else
  fail "ClickHouse no respondió '1' en http://localhost:8123/?query=SELECT%201 (usuario '$CLICKHOUSE_USER'). Salida: ${CH_OUT}"
fi

# ---------------------------------------------------------------------------
# 8. Azurite
# ---------------------------------------------------------------------------
section "8. Azurite"

AZURITE_CONTAINER="nexus-doctor-$(date +%s)"
# Well-known Azurite development account and key (public, not a secret: it is
# the same fixed value documented by Microsoft for the storage emulator).
AZURITE_CONN_STRING="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"

if command -v az >/dev/null 2>&1; then
  if az storage container create --name "$AZURITE_CONTAINER" --connection-string "$AZURITE_CONN_STRING" >/dev/null 2>&1; then
    if az storage container delete --name "$AZURITE_CONTAINER" --connection-string "$AZURITE_CONN_STRING" >/dev/null 2>&1; then
      ok "Azurite acepta crear y borrar un contenedor Blob de prueba (az storage container create/delete)."
    else
      fail "Azurite aceptó crear el contenedor de prueba pero falló al borrarlo ('$AZURITE_CONTAINER'). Límpialo a mano."
    fi
  else
    fail "Azurite no aceptó crear un contenedor Blob de prueba vía 'az storage container create'."
  fi
else
  # No Azure CLI available: degrade to a plain reachability check instead of
  # hand-rolling the Shared Key request-signing algorithm, which would be
  # fragile to maintain and cannot be validated end-to-end in this change.
  echo "INFO: 'az' CLI no disponible — comprobación simplificada a 'el puerto responde' (sin validar escritura real de blobs)."
  # No -f here: an anonymous GET on the account root without a signed request
  # legitimately gets 400 from Azurite's Blob API — that 400 (tagged with the
  # Azurite-Blob server header) is itself the proof the service is up and
  # talking the right protocol, not a failure.
  AZURITE_HEADERS="$(curl -sS -D - -o /dev/null "http://localhost:10000/devstoreaccount1" 2>&1)"
  if grep -qi '^Server: Azurite-Blob' <<< "$AZURITE_HEADERS"; then
    ok "Azurite responde en el puerto 10000 (comprobación simplificada, instala 'az' CLI para la prueba completa de create/delete)."
  else
    fail "Azurite no responde en http://localhost:10000/devstoreaccount1. Cabeceras: ${AZURITE_HEADERS}"
  fi
fi

# ---------------------------------------------------------------------------
# 9. Keycloak — token for usera
# ---------------------------------------------------------------------------
section "9. Keycloak (token de usera)"

TOKEN_RESPONSE="$(curl -sf -X POST "$KEYCLOAK_TOKEN_URL" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=password" \
  --data-urlencode "client_id=${KEYCLOAK_CLIENT_ID}" \
  --data-urlencode "username=usera" \
  --data-urlencode "password=usera" 2>&1)"
TOKEN_RC=$?

if [[ $TOKEN_RC -ne 0 ]]; then
  fail "no se pudo obtener un token de Keycloak para 'usera' (client_id='${KEYCLOAK_CLIENT_ID}') en $KEYCLOAK_TOKEN_URL. Salida: ${TOKEN_RESPONSE}"
else
  KEYCLOAK_CHECK_OUT="$(TOKEN_RESPONSE_DATA="$TOKEN_RESPONSE" EXPECTED_OID="$EXPECTED_OID" EXPECTED_AUD="$EXPECTED_AUD" python3 - <<'PY'
import base64
import json
import os
import sys

expected_oid = os.environ["EXPECTED_OID"]
expected_aud = os.environ["EXPECTED_AUD"]

# Via env var, not stdin piped into this heredoc — see the comment on the
# container-health check above for why that combination silently drops the
# piped data.
raw = os.environ["TOKEN_RESPONSE_DATA"]
try:
    body = json.loads(raw)
except json.JSONDecodeError:
    print(f"FAIL:respuesta de Keycloak no es JSON: {raw!r}")
    sys.exit(1)

access_token = body.get("access_token")
if not access_token:
    print(f"FAIL:la respuesta de Keycloak no trae 'access_token': {body}")
    sys.exit(1)

parts = access_token.split(".")
if len(parts) != 3:
    print("FAIL:access_token no tiene forma de JWT (3 partes separadas por '.').")
    sys.exit(1)

payload_b64 = parts[1]
padding = "=" * (-len(payload_b64) % 4)
try:
    payload = json.loads(base64.urlsafe_b64decode(payload_b64 + padding))
except Exception as exc:
    print(f"FAIL:no se pudo decodificar el payload del JWT: {exc}")
    sys.exit(1)

oid = payload.get("oid")
aud = payload.get("aud")
aud_list = aud if isinstance(aud, list) else [aud]

errors = []
if oid != expected_oid:
    errors.append(f"oid esperado '{expected_oid}', obtenido '{oid}'")
if expected_aud not in aud_list:
    errors.append(f"aud esperado que incluya '{expected_aud}', obtenido '{aud}'")

if errors:
    print("FAIL:" + "; ".join(errors))
    sys.exit(1)

print(f"OK:token de usera valido con oid='{oid}' y aud='{aud}'")
sys.exit(0)
PY
)"
  KEYCLOAK_RC=$?
  KEYCLOAK_STATUS="${KEYCLOAK_CHECK_OUT%%:*}"
  KEYCLOAK_DETAIL="${KEYCLOAK_CHECK_OUT#*:}"
  if [[ $KEYCLOAK_RC -eq 0 && "$KEYCLOAK_STATUS" == "OK" ]]; then
    ok "$KEYCLOAK_DETAIL"
  else
    fail "$KEYCLOAK_DETAIL"
  fi
fi

# ---------------------------------------------------------------------------
# 10. Aspire Dashboard (otel)
# ---------------------------------------------------------------------------
# The otel service has no healthcheck at all (its image has no shell to run
# one in — see docker-compose.yml), so this is the only thing that actually
# verifies it is alive, not just "container running".
section "10. Aspire Dashboard (otel)"

if curl -sf -o /dev/null "http://localhost:18888/"; then
  ok "Aspire Dashboard responde en http://localhost:18888/."
else
  fail "Aspire Dashboard no responde en http://localhost:18888/."
fi

if nc -z -w2 localhost 4317 2>/dev/null; then
  ok "Puerto OTLP gRPC 4317 acepta conexiones."
else
  fail "Puerto OTLP gRPC 4317 no acepta conexiones."
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo
if [[ "$FAILED" -ne 0 ]]; then
  echo "RESUMEN: una o más comprobaciones han FALLADO. Revisa los mensajes anteriores."
  exit 1
fi

echo "RESUMEN: todas las comprobaciones están OK."
exit 0
