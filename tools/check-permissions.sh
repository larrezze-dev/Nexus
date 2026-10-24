#!/usr/bin/env bash
# check-permissions.sh — Verifies that every x-permission used in a service
# OpenAPI contract exists as a code in contracts/permissions.yaml.
#
# Usage: tools/check-permissions.sh   (run from anywhere, it anchors to the repo root)
set -euo pipefail

cd "$(dirname "$0")/.."

PERMISSIONS_FILE="contracts/permissions.yaml"
OPENAPI_DIR="contracts/openapi"

if [[ ! -f "$PERMISSIONS_FILE" ]]; then
  echo "WARN: $PERMISSIONS_FILE not found yet — skipping check."
  exit 0
fi

# Extract declared permission codes: entries of the form "code: SOME_CODE".
declared_codes=()
while IFS= read -r line; do
  [[ -n "$line" ]] && declared_codes+=("$line")
done < <(grep -oE '^[[:space:]]*-?[[:space:]]*code:[[:space:]]*[A-Za-z0-9_:]+' "$PERMISSIONS_FILE" \
  | sed -E 's/^[[:space:]]*-?[[:space:]]*code:[[:space:]]*//' | sort -u)

if [[ ! -d "$OPENAPI_DIR" ]]; then
  echo "INFO: $OPENAPI_DIR not found yet — nothing to check."
  exit 0
fi

# Service contract files: directly under contracts/openapi/, excluding the
# shared components directory (_shared), any other leading-underscore dir, and
# dotfiles such as .spectral.yaml (find's -name matches leading dots, unlike
# shell globbing, so ".spectral.yaml" would otherwise be treated as a service
# contract and scanned for x-permission -- same fix already applied to the
# Makefile's own `contracts` target).
service_files=()
while IFS= read -r f; do
  [[ -n "$f" ]] && service_files+=("$f")
done < <(find "$OPENAPI_DIR" -maxdepth 1 -type f -name '*.yaml' ! -name '.*' 2>/dev/null | sort)

if [[ "${#service_files[@]}" -eq 0 ]]; then
  echo "INFO: no service OpenAPI files under $OPENAPI_DIR yet — nothing to check."
  exit 0
fi

if [[ "${#declared_codes[@]}" -eq 0 ]]; then
  echo "INFO: $PERMISSIONS_FILE has no codes yet — nothing to validate against."
  exit 0
fi

failed=0

for file in "${service_files[@]}"; do
  used_permissions=()
  while IFS= read -r p; do
    [[ -n "$p" ]] && used_permissions+=("$p")
  done < <(grep -oE 'x-permission:[[:space:]]*"?[A-Za-z0-9_:]+"?' "$file" \
    | sed -E 's/x-permission:[[:space:]]*"?//; s/"$//' | sort -u)

  # Guard against bash's "unbound variable" on an empty array under `set -u`
  # (bash < 4.4, e.g. macOS's stock /bin/bash 3.2) if a service file somehow
  # has zero x-permission matches.
  if [[ "${#used_permissions[@]}" -gt 0 ]]; then
    for perm in "${used_permissions[@]}"; do
      # PUBLIC and AUTHENTICATED are reserved values (conventions-api.md §3),
      # not resource codes -- they never need a contracts/permissions.yaml entry.
      [[ "$perm" == "PUBLIC" || "$perm" == "AUTHENTICATED" ]] && continue
      if ! printf '%s\n' "${declared_codes[@]}" | grep -qx -- "$perm"; then
        echo "ERROR: x-permission '$perm' in $file is not declared in $PERMISSIONS_FILE." >&2
        failed=1
      fi
    done
  fi
done

if [[ "$failed" -ne 0 ]]; then
  echo "FAILED: one or more x-permission values are not declared. See errors above." >&2
  exit 1
fi

echo "OK: all x-permission values used in $OPENAPI_DIR/*.yaml are declared in $PERMISSIONS_FILE."
exit 0
