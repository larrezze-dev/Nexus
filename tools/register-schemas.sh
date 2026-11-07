#!/usr/bin/env bash
# register-schemas.sh — Registers each topic's Avro schema in the local
# Schema Registry, as subject "<topic>-value" (TopicNameStrategy), per
# conventions-events.md §6.
#
# Usage: tools/register-schemas.sh   (run from anywhere, it anchors to the repo root)
# Env:   SCHEMA_REGISTRY_URL   override the registry URL (default http://localhost:8081)
set -euo pipefail

cd "$(dirname "$0")/.."

TOPICS_FILE="contracts/events/topics.yaml"
SCHEMA_REGISTRY_URL="${SCHEMA_REGISTRY_URL:-http://localhost:8081}"

if [[ ! -f "$TOPICS_FILE" ]]; then
  echo "WARN: $TOPICS_FILE not found yet — skipping schema registration."
  exit 0
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "ERROR: jq is required to safely embed .avsc content as a JSON string (avoids hand-rolled escaping). Install jq and retry." >&2
  exit 1
fi

# Extract (name, schema) pairs. Each topic entry starts with a "  - name:"
# line; the "schema:" line that follows it (before the next "  - name:")
# belongs to it. Plain grep/sed/awk scanning, no YAML library — same
# technique as check-permissions.sh's "code:" extraction.
names=()
schemas=()
current_name=""
while IFS= read -r line; do
  if [[ "$line" =~ ^[[:space:]]*-[[:space:]]*name:[[:space:]]*(.+)$ ]]; then
    current_name="$(echo "${BASH_REMATCH[1]}" | sed -E 's/[[:space:]]+$//')"
  elif [[ "$line" =~ ^[[:space:]]+schema:[[:space:]]*(.+)$ ]] && [[ -n "$current_name" ]]; then
    schema_path="$(echo "${BASH_REMATCH[1]}" | sed -E 's/[[:space:]]+$//')"
    names+=("$current_name")
    schemas+=("$schema_path")
    current_name=""
  fi
done < "$TOPICS_FILE"

if [[ "${#names[@]}" -eq 0 ]]; then
  echo "INFO: no topic entries with a schema: field found in $TOPICS_FILE yet — nothing to register."
  exit 0
fi

# Safety net: the scan above only associates a "schema:" field with its entry
# if "name:" is that entry's first key, so it can only ever find one pair per
# "- name:" line it sees -- comparing the pair count against that SAME line
# count can never catch a missed entry (it's the same number by
# construction). Compare instead against the total number of entry-start
# lines ("- <any key>:"), the same count tools/validate-topics.sh uses to
# catch this: an entry whose first key isn't "name" shows up there but not
# among the pairs found above.
entry_start_count=$(grep -cE '^[[:space:]]*-[[:space:]]+[a-zA-Z_]+:' "$TOPICS_FILE" || true)
if [[ "${#names[@]}" -ne "$entry_start_count" ]]; then
  echo "ERROR: found ${#names[@]} (name, schema) pairs but $TOPICS_FILE has $entry_start_count topic entries total — some entry was not recognized (every entry must list 'name' as its first field; run tools/validate-topics.sh)." >&2
  exit 1
fi

failed=0
registered=0
tmp_resp="$(mktemp)"
trap 'rm -f "$tmp_resp"' EXIT

for i in "${!names[@]}"; do
  name="${names[$i]}"
  schema_path="${schemas[$i]}"
  subject="${name}-value"

  if [[ ! -f "$schema_path" ]]; then
    echo "ERROR: schema file '$schema_path' for topic '$name' does not exist." >&2
    failed=1
    continue
  fi

  echo "Registering $schema_path as subject $subject ..."

  # Set the subject's compatibility mode explicitly (D8: BACKWARD is
  # mandatory), rather than relying on the registry's global default. The
  # local registry's global default happens to already be "backward" (see
  # infra/compose/docker-compose.yml), but that's an environment setting, not
  # a per-subject guarantee -- a differently-configured registry (e.g. a
  # future Azure one, 0.11) would silently skip compatibility checking
  # entirely without this. Idempotent: always sets the same value.
  compat_http_code="$(curl -s -o "$tmp_resp" -w '%{http_code}' \
    -X PUT "$SCHEMA_REGISTRY_URL/config/${subject}" \
    -H 'Content-Type: application/vnd.schemaregistry.v1+json' \
    --data-binary '{"compatibility":"BACKWARD"}')"
  if [[ "$compat_http_code" -lt 200 || "$compat_http_code" -ge 300 ]]; then
    echo "ERROR: setting BACKWARD compatibility on $subject failed (HTTP $compat_http_code):" >&2
    cat "$tmp_resp" >&2
    echo >&2
    failed=1
    continue
  fi

  # Embed the .avsc raw content as a JSON string value via jq -Rs (reads
  # stdin as a single raw string), rather than hand-rolling escaping.
  http_code="$(jq -Rs '{schema: ., schemaType: "AVRO"}' < "$schema_path" \
    | curl -s -o "$tmp_resp" -w '%{http_code}' \
      -X POST "$SCHEMA_REGISTRY_URL/subjects/${subject}/versions" \
      -H 'Content-Type: application/vnd.schemaregistry.v1+json' \
      --data-binary @-)"

  if [[ "$http_code" -lt 200 || "$http_code" -ge 300 ]]; then
    echo "ERROR: registering $subject failed (HTTP $http_code):" >&2
    cat "$tmp_resp" >&2
    echo >&2
    failed=1
    continue
  fi

  registered=$((registered + 1))
done

if [[ "$failed" -ne 0 ]]; then
  echo "FAILED: one or more schemas could not be registered. See errors above." >&2
  exit 1
fi

echo "OK: registered $registered schemas."
exit 0
