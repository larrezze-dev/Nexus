#!/usr/bin/env bash
# rule-coverage.sh — Finds the step card for a given step id, extracts every
# business-rule id (pattern [A-Z]{2,4}-R[0-9]+) it cites, and checks that each
# one is referenced by at least one test file. Lists the uncovered ids and
# exits 1 if any remain; exits 0 otherwise (including when the card has no
# rule ids at all, e.g. step 0.1).
#
# Usage:
#   tools/rule-coverage.sh STEP=0.1
#   STEP=0.1 tools/rule-coverage.sh
#   tools/rule-coverage.sh --step=0.1
set -euo pipefail

cd "$(dirname "$0")/.."

STEP="${STEP:-}"

for arg in "$@"; do
  case "$arg" in
    STEP=*) STEP="${arg#STEP=}" ;;
    --step=*) STEP="${arg#--step=}" ;;
    *) ;;
  esac
done

if [[ -z "$STEP" ]]; then
  echo "ERROR: missing STEP. Usage: tools/rule-coverage.sh STEP=<id>" >&2
  exit 1
fi

card=$(find docs/steps -type f -path "*/F*/${STEP}-*.md" 2>/dev/null | sort | head -n1)

if [[ -z "$card" ]]; then
  echo "ERROR: no step card found for STEP=${STEP} under docs/steps/F*/${STEP}-*.md" >&2
  exit 1
fi

echo "Step card: $card"

rule_ids=()
while IFS= read -r line; do
  [[ -n "$line" ]] && rule_ids+=("$line")
done < <(grep -oE '[A-Z]{2,4}-R[0-9]+' "$card" | sort -u)

if [[ "${#rule_ids[@]}" -eq 0 ]]; then
  echo "OK: step ${STEP} has no XXX-Rn business rules to cover."
  exit 0
fi

is_covered() {
  local id="$1"

  if [[ -d src ]]; then
    local test_dirs=()
    while IFS= read -r d; do
      [[ -n "$d" ]] && test_dirs+=("$d")
    done < <(find src -type d -name '*Tests*' 2>/dev/null)
    if [[ "${#test_dirs[@]}" -gt 0 ]] && grep -rl -- "$id" "${test_dirs[@]}" 2>/dev/null | grep -q .; then
      return 0
    fi
  fi

  if [[ -d web ]]; then
    local web_specs=()
    while IFS= read -r f; do
      [[ -n "$f" ]] && web_specs+=("$f")
    done < <(find web -type f -name '*.spec.ts' 2>/dev/null)
    if [[ "${#web_specs[@]}" -gt 0 ]] && grep -l -- "$id" "${web_specs[@]}" 2>/dev/null | grep -q .; then
      return 0
    fi
  fi

  if [[ -d solver/src/test ]] && grep -rl -- "$id" solver/src/test 2>/dev/null | grep -q .; then
    return 0
  fi

  if [[ -d tests/e2e ]] && grep -rl -- "$id" tests/e2e 2>/dev/null | grep -q .; then
    return 0
  fi

  return 1
}

uncovered=()
for id in "${rule_ids[@]}"; do
  if ! is_covered "$id"; then
    uncovered+=("$id")
  fi
done

if [[ "${#uncovered[@]}" -eq 0 ]]; then
  echo "OK: all ${#rule_ids[@]} rule(s) cited in step ${STEP} are covered by a test."
  exit 0
fi

echo "FAILED: ${#uncovered[@]} rule(s) cited in step ${STEP} have no covering test:" >&2
for id in "${uncovered[@]}"; do
  echo "  - $id" >&2
done
exit 1
