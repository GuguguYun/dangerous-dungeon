#!/bin/zsh
# Build the Unity client, run the headless self-test, then capture screenshots.
# Usage: scripts/check.sh [shot-name]   (screenshots land in logs/shots/<shot-name>, default "latest")
set -u
ROOT=${0:A:h:h}
UNITY=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity
BIN="$ROOT/unity/Builds/MeshyDungeon.app/Contents/MacOS/Meshy Dungeon"
SHOTS="$ROOT/logs/shots/${1:-latest}"
mkdir -p "$ROOT/logs"

# Run a command, killing it after N seconds (macOS has no timeout(1)).
run_for() {
  local secs=$1; shift
  "$@" & local pid=$!
  for i in $(seq 1 $secs); do kill -0 $pid 2>/dev/null || break; /bin/sleep 1; done
  kill $pid 2>/dev/null
  wait $pid
}

echo "== build"
"$UNITY" -batchmode -quit -nographics -projectPath "$ROOT/unity" -executeMethod BuildScript.BuildMac -logFile "$ROOT/logs/build.log" >/dev/null 2>&1
grep -E "error CS|BUILD " "$ROOT/logs/build.log" | sort -u
grep -q "BUILD Succeeded" "$ROOT/logs/build.log" || { echo "BUILD FAILED"; exit 1; }

echo "== autotest"
run_for 200 "$BIN" -batchmode -nographics -autotest -logFile "$ROOT/logs/autotest.log" >/dev/null 2>&1
grep -E "AUTOTEST (FAIL|done)|Exception" "$ROOT/logs/autotest.log"

echo "== shots"
rm -rf "$SHOTS"; mkdir -p "$SHOTS"
run_for 240 "$BIN" -shots "$SHOTS" -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -logFile "$ROOT/logs/shots.log" >/dev/null 2>&1
grep -E "SHOTS done|Exception" "$ROOT/logs/shots.log" | sort -u
ls "$SHOTS"
