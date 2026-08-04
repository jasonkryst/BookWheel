#!/usr/bin/env bash
# Fails the calling process when a `dotnet list package --vulnerable` report
# indicates vulnerable packages. `dotnet list --vulnerable` always exits 0,
# even when it finds vulnerabilities, so this script is the actual gate.
#
# Usage:
#   dotnet list <project> package --vulnerable --include-transitive | scripts/check-vulnerable-packages.sh
#   scripts/check-vulnerable-packages.sh <project1.csproj> [project2.csproj ...]
set -uo pipefail

MARKER="has the following vulnerable packages"

check_report() {
  local report="$1"
  printf '%s\n' "$report"
  if grep -qF "$MARKER" <<< "$report"; then
    return 1
  fi
  return 0
}

if [ "$#" -eq 0 ]; then
  report="$(cat)"
  check_report "$report"
  exit $?
fi

status=0
for project in "$@"; do
  echo "== $project =="
  report="$(dotnet list "$project" package --vulnerable --include-transitive)"
  if ! check_report "$report"; then
    status=1
  fi
done
exit "$status"
