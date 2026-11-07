#!/usr/bin/env bash
# create-topics.sh — Creates local Kafka topics (+ their DLQ) from
# contracts/events/topics.yaml (conventions-events.md §4/§5).
#
# Usage: tools/create-topics.sh   (run from anywhere, it anchors to the repo root)
set -euo pipefail

cd "$(dirname "$0")/.."

TOPICS_FILE="contracts/events/topics.yaml"
KAFKA_CONTAINER="nexus-kafka"
KAFKA_BIN="/opt/kafka/bin/kafka-topics.sh"
# The PLAINTEXT_HOST listener's advertised address is "localhost:${KAFKA_PORT}" (see
# infra/compose/docker-compose.yml), which only resolves correctly from inside the
# container when KAFKA_PORT is the default 9092. If a developer overrides KAFKA_PORT in
# .env, kafka-topics.sh (run via `docker exec`, i.e. already inside the container) would
# bootstrap fine on :9092 but then fail reconnecting to the advertised "localhost:<port>"
# for the actual topic operations. The internal PLAINTEXT listener's advertised address
# ("kafka:29092") is NOT parameterized by KAFKA_PORT, so it always resolves correctly here.
BOOTSTRAP="kafka:29092"

if [[ ! -f "$TOPICS_FILE" ]]; then
  echo "WARN: $TOPICS_FILE not found yet — skipping topic creation."
  exit 0
fi

if ! docker ps --format '{{.Names}}' | grep -qx "$KAFKA_CONTAINER"; then
  echo "ERROR: container '$KAFKA_CONTAINER' is not running — run 'make up' first." >&2
  exit 1
fi

# Parse topics.yaml with awk only (no YAML library — same no-dependency
# technique as check-permissions.sh's "code:" extraction). Each entry starts
# with "- name: ..." and, further down, has a "partitions:" block with a
# "local:" value before the "azure:" key. We track the current name/local
# as we scan line by line and emit "name<TAB>local" whenever we hit the next
# "- name:" line or EOF.
pairs=$(awk '
  /^[[:space:]]*-[[:space:]]*name:/ {
    if (name != "") print name "\t" local
    name = $0
    sub(/^[[:space:]]*-[[:space:]]*name:[[:space:]]*/, "", name)
    local = ""
    next
  }
  /^[[:space:]]*local:[[:space:]]*[0-9]+/ {
    val = $0
    sub(/^[[:space:]]*local:[[:space:]]*/, "", val)
    local = val
    next
  }
  END {
    if (name != "") print name "\t" local
  }
' "$TOPICS_FILE")

if [[ -z "$pairs" ]]; then
  echo "INFO: no topics found in $TOPICS_FILE — nothing to create."
  exit 0
fi

# Safety net: the awk parser above only recognizes an entry if "name:" is its
# first key, so it can only ever emit one pair per "- name:" line it sees --
# comparing the pair count against that SAME line count can never catch a
# missed entry (it's the same number by construction). Compare instead
# against the total number of entry-start lines ("- <any key>:"), the same
# count tools/validate-topics.sh uses to catch this: an entry whose first key
# isn't "name" shows up there but not in $pairs.
pair_count=$(printf '%s\n' "$pairs" | grep -c . || true)
entry_start_count=$(grep -cE '^[[:space:]]*-[[:space:]]+[a-zA-Z_]+:' "$TOPICS_FILE" || true)
if [[ "$pair_count" -ne "$entry_start_count" ]]; then
  echo "ERROR: parsed $pair_count topic entries but $TOPICS_FILE has $entry_start_count topic entries total — some entry was not recognized (every entry must list 'name' as its first field; run tools/validate-topics.sh)." >&2
  exit 1
fi

created=0
failed=0

while IFS=$'\t' read -r name local_partitions; do
  [[ -z "$name" ]] && continue
  if [[ -z "$local_partitions" ]]; then
    echo "ERROR: topic '$name' has no partitions.local value in $TOPICS_FILE." >&2
    failed=1
    continue
  fi

  echo "Creating topic '$name' ($local_partitions partition(s), retention=infinite)..."
  if ! docker exec "$KAFKA_CONTAINER" "$KAFKA_BIN" --bootstrap-server "$BOOTSTRAP" \
    --create --if-not-exists --topic "$name" --partitions "$local_partitions" \
    --replication-factor 1 --config retention.ms=-1; then
    echo "ERROR: failed to create topic '$name'." >&2
    failed=1
    continue
  fi
  created=$((created + 1))

  dlq_name="${name}.DLQ"
  echo "Creating DLQ topic '$dlq_name' ($local_partitions partition(s), retention=14d)..."
  if ! docker exec "$KAFKA_CONTAINER" "$KAFKA_BIN" --bootstrap-server "$BOOTSTRAP" \
    --create --if-not-exists --topic "$dlq_name" --partitions "$local_partitions" \
    --replication-factor 1 --config retention.ms=1209600000; then
    echo "ERROR: failed to create DLQ topic '$dlq_name'." >&2
    failed=1
    continue
  fi
  created=$((created + 1))
done <<< "$pairs"

if [[ "$failed" -ne 0 ]]; then
  echo "FAILED: one or more topics could not be created. See errors above." >&2
  exit 1
fi

echo "OK: $created topic(s) (domain + DLQ) created or already present in Kafka."
exit 0
