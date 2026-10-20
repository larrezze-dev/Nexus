#!/usr/bin/env bash
# check-packages.sh — Verifies that src/Directory.Packages.props only references
# NuGet packages from the approved list (tools/allowed-packages.txt) and never
# the licenses forbidden by ADR-0007 (MediatR, AutoMapper, FluentAssertions, MassTransit).
#
# Usage: tools/check-packages.sh   (run from anywhere, it anchors to the repo root)
set -euo pipefail

cd "$(dirname "$0")/.."

PACKAGES_PROPS="src/Directory.Packages.props"
ALLOWED_LIST="tools/allowed-packages.txt"

if [[ ! -f "$PACKAGES_PROPS" ]]; then
  echo "WARN: $PACKAGES_PROPS not found yet (not created by this step) — skipping check."
  exit 0
fi

if [[ ! -f "$ALLOWED_LIST" ]]; then
  echo "ERROR: $ALLOWED_LIST not found. Cannot validate packages without the approved list." >&2
  exit 1
fi

# Extract declared package names: <PackageVersion Include="Name" ... />
declared_packages=$(grep -oE '<PackageVersion[[:space:]]+Include="[^"]+"' "$PACKAGES_PROPS" \
  | sed -E 's/<PackageVersion[[:space:]]+Include="([^"]+)"/\1/' || true)

if [[ -z "$declared_packages" ]]; then
  echo "WARN: no <PackageVersion Include=\"...\" /> entries found in $PACKAGES_PROPS — nothing to check."
  exit 0
fi

failed=0

# Explicit, named check for the packages forbidden by ADR-0007, so the error
# message is specific about the license ban even though (b) is already covered
# by the allow-list check below.
forbidden_packages=(MediatR AutoMapper FluentAssertions MassTransit)
for forbidden in "${forbidden_packages[@]}"; do
  if printf '%s\n' "$declared_packages" | grep -qx "$forbidden"; then
    echo "ERROR: '$forbidden' is forbidden by ADR-0007 and must not appear in $PACKAGES_PROPS." >&2
    failed=1
  fi
done

# Allow-list check: every declared package must be in tools/allowed-packages.txt.
while IFS= read -r package; do
  [[ -z "$package" ]] && continue
  if ! grep -qx "$package" "$ALLOWED_LIST"; then
    echo "ERROR: package '$package' declared in $PACKAGES_PROPS is not in $ALLOWED_LIST." >&2
    failed=1
  fi
done <<< "$declared_packages"

if [[ "$failed" -ne 0 ]]; then
  echo "FAILED: one or more packages are not allowed. See errors above." >&2
  exit 1
fi

echo "OK: all packages in $PACKAGES_PROPS are on the approved list."
exit 0
