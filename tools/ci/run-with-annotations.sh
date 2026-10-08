#!/usr/bin/env bash
#
# Runs a command, streams its output and, if the command fails, mirrors that output into check-run
# annotations. The GitHub Actions job log lives on blob storage, which is not reachable from every
# environment (an agent sandbox, for instance), while annotations are readable through the REST API.
#
# Usage:  tools/ci/run-with-annotations.sh <command> [args...]
set -uo pipefail

log="$(mktemp)"
"$@" 2>&1 | tee "$log"
status="${PIPESTATUS[0]}"

if [ "$status" -ne 0 ]; then
  echo "::error::$1 exited with status $status"
  while IFS= read -r line; do
    [ -n "$line" ] && printf '::error::%s\n' "$line"
  done < "$log"
fi

rm -f "$log"
exit "$status"
