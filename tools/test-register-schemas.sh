#!/usr/bin/env bash
# test-register-schemas.sh — proves tools/register-schemas.sh's content lookup
# against a scratch Kafka + Schema Registry (same images as
# infra/compose/docker-compose.yml, own network, non-default port, removed on exit).
# Never touches contracts/: works on a scratch NEXUS_REG_REPO_ROOT copy.
#
# Usage: tools/test-register-schemas.sh
# Env:   TEST_SR_PORT   host port for the scratch registry (default 18081)
# Needs: docker, jq, curl.
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
PORT="${TEST_SR_PORT:-18081}"
SUF="regtest-$$"
NET="nexus-$SUF"
KAFKA="nexus-kafka-$SUF"
SR="nexus-sr-$SUF"
SCRATCH="$(mktemp -d)"
export SCHEMA_REGISTRY_URL="http://localhost:$PORT"

cleanup() {
  docker rm -f "$SR" "$KAFKA" >/dev/null 2>&1 || true
  docker network rm "$NET" >/dev/null 2>&1 || true
  rm -rf "$SCRATCH"
}
trap cleanup EXIT

fail() { echo "TEST FAIL: $*" >&2; exit 1; }

echo "Starting scratch Kafka + Schema Registry ($NET, port $PORT) ..."
docker network create "$NET" >/dev/null
docker run -d --name "$KAFKA" --network "$NET" --network-alias kafka \
  -e KAFKA_NODE_ID=1 -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://:29092,CONTROLLER://:9093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://kafka:29092 \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@kafka:9093 \
  -e KAFKA_INTER_BROKER_LISTENER_NAME=PLAINTEXT \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 \
  -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 \
  -e KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0 \
  apache/kafka:4.3.2 >/dev/null
docker run -d --name "$SR" --network "$NET" -p "$PORT:8081" \
  -e SCHEMA_REGISTRY_HOST_NAME=schema-registry \
  -e SCHEMA_REGISTRY_LISTENERS=http://0.0.0.0:8081 \
  -e SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS=kafka:29092 \
  -e SCHEMA_REGISTRY_KAFKASTORE_TOPIC_REPLICATION_FACTOR=1 \
  -e SCHEMA_REGISTRY_SCHEMA_COMPATIBILITY_LEVEL=backward \
  confluentinc/cp-schema-registry:8.3.2 >/dev/null

for _ in $(seq 1 90); do
  curl -sf "$SCHEMA_REGISTRY_URL/subjects" >/dev/null 2>&1 && break
  sleep 2
done
curl -sf "$SCHEMA_REGISTRY_URL/subjects" >/dev/null || fail "scratch registry did not become ready"

# Scratch repo root with a single topic, copied from the real contracts.
SRC_SCHEMA="contracts/events/org/party.created.v1.avsc"
mkdir -p "$SCRATCH/contracts/events/org"
cp "$HERE/$SRC_SCHEMA" "$SCRATCH/$SRC_SCHEMA"
cat > "$SCRATCH/contracts/events/topics.yaml" <<YAML
topics:
  - name: nexus.org.party.created
    schema: $SRC_SCHEMA
YAML
SUBJECT="nexus.org.party.created-value"
run_register() { NEXUS_REG_REPO_ROOT="$SCRATCH" bash "$HERE/tools/register-schemas.sh"; }

echo "== (1) register + lookup returns the id"
out="$(run_register)"; echo "$out"
echo "$out" | grep -Eq "^OK $SUBJECT id=[1-9][0-9]* subject-version=1$" || fail "(1) expected OK line with subject-version=1"
id1="$(echo "$out" | sed -En "s/^OK $SUBJECT id=([0-9]+) .*/\1/p")"

echo "== (2) compatible evolution registers subject version 2; lookup by content is independent of numbering"
cp "$SCRATCH/$SRC_SCHEMA" "$SCRATCH/old.avsc"
jq '.fields += [{"name":"scratch_extra","type":["null","string"],"default":null}]' "$SCRATCH/old.avsc" > "$SCRATCH/new.avsc"
cp "$SCRATCH/new.avsc" "$SCRATCH/$SRC_SCHEMA"
out="$(run_register)"; echo "$out"
echo "$out" | grep -Eq "^OK $SUBJECT id=[1-9][0-9]* subject-version=2$" || fail "(2) expected subject-version=2 after evolving"
id2="$(echo "$out" | sed -En "s/^OK $SUBJECT id=([0-9]+) .*/\1/p")"
[[ "$id1" != "$id2" ]] || fail "(2) old and new content must have different ids"

# shellcheck source=tools/register-schemas.sh
REGISTER_SCHEMAS_LIB=1 source "$HERE/tools/register-schemas.sh"
old_out="$(verify_lookup "$SUBJECT" "$SCRATCH/old.avsc")"; echo "old: $old_out"
new_out="$(verify_lookup "$SUBJECT" "$SCRATCH/new.avsc")"; echo "new: $new_out"
[[ "$old_out" == "OK $SUBJECT id=$id1 subject-version=1" ]] || fail "(2) old content must still resolve to id=$id1 version 1"
[[ "$new_out" == "OK $SUBJECT id=$id2 subject-version=2" ]] || fail "(2) new content must resolve to id=$id2 version 2"

echo "== (3) never-registered content returns 404 and verify_lookup fails"
jq '.fields += [{"name":"never_registered","type":"string","default":""}]' "$SCRATCH/old.avsc" > "$SCRATCH/unregistered.avsc"
code="$(jq -Rs '{schema: ., schemaType: "AVRO"}' < "$SCRATCH/unregistered.avsc" \
  | curl -s -o /dev/null -w '%{http_code}' -X POST "$SCHEMA_REGISTRY_URL/subjects/$SUBJECT" \
    -H 'Content-Type: application/vnd.schemaregistry.v1+json' --data-binary @-)"
[[ "$code" == "404" ]] || fail "(3) expected raw HTTP 404, got $code"
if verify_lookup "$SUBJECT" "$SCRATCH/unregistered.avsc" 2>"$SCRATCH/err"; then
  fail "(3) verify_lookup should have failed for unregistered content"
fi
cat "$SCRATCH/err"
grep -q "HTTP 404" "$SCRATCH/err" || fail "(3) error should mention HTTP 404"

echo "ALL TESTS PASSED"
