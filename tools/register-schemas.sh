#!/usr/bin/env bash
# register-schemas.sh — Registers each topic's Avro schema in the local
# Schema Registry, as subject "<topic>-value" (TopicNameStrategy), per
# conventions-events.md §6.
#
# Usage: tools/register-schemas.sh   (run from anywhere, it anchors to the repo root)
# Env:   SCHEMA_REGISTRY_URL   override the registry URL (default http://localhost:8081)
#        NEXUS_REG_REPO_ROOT   directory the script anchors to (default: the repo root);
#                              the topics file and the schema paths in it resolve against it.
#                              Used by tools/test-register-schemas.sh to run on scratch inputs.
#        NEXUS_REG_TOPICS_FILE topics registry (default contracts/events/topics.yaml).
#                              If either NEXUS_REG_* override is set and the topics file
#                              is missing, the script fails (exit 1) instead of skipping.
#        REGISTER_SCHEMAS_LIB=1  only honoured when the script is SOURCED (library mode:
#                              defines verify_lookup and returns). Executing the script
#                              with it set is an error (exit 2).
#
# Two independent numberings, and why verification is by content:
#   - The file name "v<n>" (e.g. foo.v2.avsc) is the outbox `schema_version` --
#     the producer's own contract version, bumped on every schema change.
#   - The registry's subject version (1, 2, 3, ...) is just the order in which
#     distinct contents were registered under "<topic>-value". It is assigned by
#     the registry, and need not equal <n> (e.g. a v2 file may be the subject's
#     first registered version, or a topic may skip versions).
# They are unrelated, so nothing may be derived from one to find the other.
# The relay resolves a writer schema BY CONTENT: POST /subjects/<subject> with
# {"schema": "<avsc as JSON string>", "schemaType": "AVRO"} returns 200 with
# {subject, id, version, schema} when exactly that content is registered under
# the subject (404 / error 40403 otherwise). After every registration this
# script performs that same lookup with the local .avsc and fails unless it
# returns HTTP 200 and a positive integer id (ADR-0018).
SCHEMA_REGISTRY_URL="${SCHEMA_REGISTRY_URL:-http://localhost:8081}"

# verify_lookup <subject> <avsc-path>
# Looks the .avsc content up under <subject>. On success prints
# "OK <subject> id=<id> subject-version=<version>" and returns 0; otherwise
# prints an error to stderr and returns 1.
verify_lookup() {
  local subject="$1" avsc="$2" resp code id version
  resp="$(mktemp)"
  code="$(jq -Rs '{schema: ., schemaType: "AVRO"}' < "$avsc" \
    | curl -s -o "$resp" -w '%{http_code}' \
      -X POST "$SCHEMA_REGISTRY_URL/subjects/${subject}" \
      -H 'Content-Type: application/vnd.schemaregistry.v1+json' \
      --data-binary @-)" || code="000"
  if [[ "$code" != "200" ]]; then
    echo "ERROR: content lookup of $avsc under $subject failed (HTTP $code):" >&2
    cat "$resp" >&2
    echo >&2
    rm -f "$resp"
    return 1
  fi
  id="$(jq -r '.id // empty' < "$resp" 2>/dev/null || true)"
  version="$(jq -r '.version // empty' < "$resp" 2>/dev/null || true)"
  rm -f "$resp"
  if [[ ! "$id" =~ ^[1-9][0-9]*$ ]]; then
    echo "ERROR: content lookup of $avsc under $subject returned HTTP 200 but no positive integer id (got '${id}')." >&2
    return 1
  fi
  echo "OK $subject id=$id subject-version=$version"
}

# Library mode (used by tools/test-register-schemas.sh): define the functions only.
if [[ "${REGISTER_SCHEMAS_LIB:-0}" == "1" ]]; then
  if [[ "${BASH_SOURCE[0]}" != "$0" ]]; then
    return 0
  fi
  echo "ERROR: REGISTER_SCHEMAS_LIB is set but $0 is being executed, not sourced; refusing to exit 0 without registering anything. Unset it, or use 'source'." >&2
  exit 2
fi

# Strict mode only for the executed script: when sourced (library mode above) the
# caller's shell options are left untouched.
set -euo pipefail

overridden=0
if [[ -n "${NEXUS_REG_REPO_ROOT:-}" || -n "${NEXUS_REG_TOPICS_FILE:-}" ]]; then
  overridden=1
fi

cd "${NEXUS_REG_REPO_ROOT:-$(dirname "$0")/..}"

TOPICS_FILE="${NEXUS_REG_TOPICS_FILE:-contracts/events/topics.yaml}"

if [[ ! -f "$TOPICS_FILE" ]]; then
  if [[ "$overridden" -eq 1 ]]; then
    echo "ERROR: $TOPICS_FILE not found (a NEXUS_REG_* override is set, so this is not the 'not created yet' case)." >&2
    exit 1
  fi
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

  if ! verify_lookup "$subject" "$schema_path"; then
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
