#!/bin/zsh
# Run a command while holding the project's Unity lock, so two workers never build or run the
# game at the same time (a build replaces the .app under a running player).
# Usage: scripts/locked.sh <command...>     waits up to 20 minutes for the lock
ROOT=${0:A:h:h}
LOCK="$ROOT/logs/.unity.lock"
mkdir -p "$ROOT/logs"
for i in $(seq 1 240); do
  if mkdir "$LOCK" 2>/dev/null; then
    echo "$$ $(date +%H:%M:%S) $*" > "$LOCK/owner"
    trap 'rm -rf "$LOCK"' EXIT INT TERM
    "$@"
    exit $?
  fi
  /bin/sleep 5
done
echo "LOCK TIMEOUT: held by $(cat "$LOCK/owner" 2>/dev/null)"
exit 1
