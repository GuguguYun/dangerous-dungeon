#!/bin/zsh
# Replace the playable app (unity/Builds/MeshyDungeon.app) with the last checked build, unless the
# user has the game open right now.
ROOT=${0:A:h:h}
CHECK_APP="$ROOT/unity/Builds/check/MeshyDungeon.app"
PLAY_APP="$ROOT/unity/Builds/MeshyDungeon.app"
[[ -d "$CHECK_APP" ]] || { echo "PUBLISH nothing to publish"; exit 1; }
if pgrep -f "$PLAY_APP/Contents/MacOS" >/dev/null; then
  echo "PUBLISH skipped: the game is open (run scripts/publish.sh after it closes)"
  exit 2
fi
rm -rf "$PLAY_APP" && mv "$CHECK_APP" "$PLAY_APP" && echo "PUBLISH done"
