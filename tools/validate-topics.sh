#!/usr/bin/env bash
# validate-topics.sh — Validates contracts/events/topics.yaml against its
# JSON Schema and cross-checks that every real .avsc file under
# contracts/events/ has exactly one topic entry pointing at it (and vice
# versa, that every referenced schema path actually exists on disk).
#
# Usage: tools/validate-topics.sh   (run from anywhere, it anchors to the repo root)
set -euo pipefail

cd "$(dirname "$0")/.."

TOPICS_FILE="contracts/events/topics.yaml"
SCHEMA_FILE="contracts/events/topics.schema.json"

if [[ ! -f "$TOPICS_FILE" ]]; then
  echo "WARN: $TOPICS_FILE not found yet — skipping check."
  exit 0
fi

# 1. Structural validation against the JSON Schema.
if ! npx -y ajv-cli validate -s "$SCHEMA_FILE" -d "$TOPICS_FILE"; then
  echo "FAILED: $TOPICS_FILE does not match $SCHEMA_FILE. See errors above." >&2
  exit 1
fi

# 2. Cross-check avsc <-> topic entries, using grep/sed/awk only (same
# dependency-free technique tools/check-permissions.sh uses to extract
# "code:" values; here we extract "schema:" values). We keep the raw
# (non-deduplicated) list -- one entry per topic -- because step 2b below
# needs to count how many topic entries point at each .avsc file, and a
# duplicate (two topics pointing at the same schema file) must still be
# detectable; a plain `sort -u` would silently collapse it to one.
schema_paths_raw=()
while IFS= read -r p; do
  [[ -n "$p" ]] && schema_paths_raw+=("$p")
done < <(grep -oE '^[[:space:]]*schema:[[:space:]]*contracts/events/[^[:space:]]+\.avsc' "$TOPICS_FILE" \
  | sed -E 's/^[[:space:]]*schema:[[:space:]]*//')

# Deduplicated view, used only for the existence check so a missing path
# referenced by several topics is reported once, not once per topic.
schema_paths_unique=()
if [[ "${#schema_paths_raw[@]}" -gt 0 ]]; then
  while IFS= read -r p; do
    [[ -n "$p" ]] && schema_paths_unique+=("$p")
  done < <(printf '%s\n' "${schema_paths_raw[@]}" | sort -u)
fi

failed=0

# 2a. Every schema path referenced in topics.yaml must exist on disk.
for path in "${schema_paths_unique[@]}"; do
  if [[ ! -f "$path" ]]; then
    echo "ERROR: $TOPICS_FILE references schema '$path', but that file does not exist." >&2
    failed=1
  fi
done

# 2b. Every real .avsc file (excluding contracts/events/_test/) must appear
# exactly once among the (raw) schema paths extracted above.
real_avsc_files=()
while IFS= read -r f; do
  [[ -n "$f" ]] && real_avsc_files+=("$f")
done < <(find contracts/events -mindepth 2 -maxdepth 2 -name '*.avsc' -not -path '*/_test/*' 2>/dev/null | sort)

for file in "${real_avsc_files[@]}"; do
  match_count=0
  for path in "${schema_paths_raw[@]}"; do
    [[ "$path" == "$file" ]] && match_count=$((match_count + 1))
  done
  if [[ "$match_count" -eq 0 ]]; then
    echo "ERROR: $file has no matching 'schema:' entry in $TOPICS_FILE." >&2
    failed=1
  elif [[ "$match_count" -gt 1 ]]; then
    echo "ERROR: $file has $match_count matching 'schema:' entries in $TOPICS_FILE (expected exactly 1)." >&2
    failed=1
  fi
done

# 3. Every entry must list "name" as its first field. tools/create-topics.sh
# and tools/register-schemas.sh parse entries by tracking "the current entry
# started at the last '- name:' line seen" -- a structurally valid YAML entry
# that happens to list a different key first (still accepted by the JSON
# Schema above, since object key order isn't a JSON Schema concept) would be
# silently skipped by those two scripts with no error. Compare the number of
# "- name:" lines against the number of entry-start ("- <anything>:") lines.
name_first_count=$(grep -cE '^[[:space:]]*-[[:space:]]*name:' "$TOPICS_FILE" || true)
entry_start_count=$(grep -cE '^[[:space:]]*-[[:space:]]+[a-zA-Z_]+:' "$TOPICS_FILE" || true)
if [[ "$name_first_count" -ne "$entry_start_count" ]]; then
  echo "ERROR: every topic entry must list 'name' as its first field ($entry_start_count entries found, only $name_first_count start with 'name:'). tools/create-topics.sh and tools/register-schemas.sh rely on this order." >&2
  failed=1
fi

# 4. Cross-field consistency: each entry's name: and schema: must agree with
# its own context/aggregate/event/version (catches copy-paste mistakes, e.g.
# two entries with swapped schema: paths, which would otherwise register the
# wrong Avro schema under a topic's subject with no error).
records=$(awk '
  function emit() {
    if (name != "") print name "\t" context "\t" aggregate "\t" event "\t" version "\t" schema
  }
  /^[[:space:]]*-[[:space:]]*name:/ {
    emit()
    name=$0; sub(/^[[:space:]]*-[[:space:]]*name:[[:space:]]*/, "", name)
    context=""; aggregate=""; event=""; version=""; schema=""
    next
  }
  /^[[:space:]]+context:[[:space:]]*/ { context=$0; sub(/^[[:space:]]+context:[[:space:]]*/, "", context); next }
  /^[[:space:]]+aggregate:[[:space:]]*/ { aggregate=$0; sub(/^[[:space:]]+aggregate:[[:space:]]*/, "", aggregate); next }
  /^[[:space:]]+event:[[:space:]]*/ { event=$0; sub(/^[[:space:]]+event:[[:space:]]*/, "", event); next }
  /^[[:space:]]+version:[[:space:]]*/ { version=$0; sub(/^[[:space:]]+version:[[:space:]]*/, "", version); next }
  /^[[:space:]]+schema:[[:space:]]*/ { schema=$0; sub(/^[[:space:]]+schema:[[:space:]]*/, "", schema); next }
  END { emit() }
' "$TOPICS_FILE")

while IFS=$'\t' read -r name context aggregate event version schema; do
  [[ -z "$name" ]] && continue
  expected_schema="contracts/events/${context}/${aggregate}.${event}.v${version}.avsc"
  if [[ "$version" == "1" ]]; then
    expected_name="nexus.${context}.${aggregate}.${event}"
  else
    expected_name="nexus.${context}.${aggregate}.${event}.v${version}"
  fi
  if [[ "$schema" != "$expected_schema" ]]; then
    echo "ERROR: topic '$name' has schema: '$schema', expected '$expected_schema' (derived from its own context/aggregate/event/version fields)." >&2
    failed=1
  fi
  if [[ "$name" != "$expected_name" ]]; then
    echo "ERROR: topic '$name' is inconsistent with its own context/aggregate/event/version fields (expected name '$expected_name')." >&2
    failed=1
  fi
done <<< "$records"

if [[ "$failed" -ne 0 ]]; then
  echo "FAILED: one or more avsc/topic cross-check errors. See errors above." >&2
  exit 1
fi

echo "OK: topics.yaml is valid and every .avsc has exactly one topic entry."
exit 0
