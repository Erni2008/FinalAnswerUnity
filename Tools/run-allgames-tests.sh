#!/bin/zsh
set -eu
project="$(cd "$(dirname "$0")/.." && pwd)"
editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
curl -fsS http://127.0.0.1:18096/health >/dev/null
mkdir -p "$project/Artifacts"
python3 - "$project" <<'PY'
from pathlib import Path
import sys
(Path(sys.argv[1])/'Artifacts/allgames-room.json').unlink(missing_ok=True)
PY
node "$project/Tools/check-allgames-browser.cjs" > "$project/Artifacts/allgames-browser.log" 2>&1 &
browser_pid=$!
trap 'kill "$browser_pid" 2>/dev/null || true' EXIT
"$editor" -batchmode -projectPath "$project" -runTests -testPlatform PlayMode -testFilter FinalAnswer.Tests.AllGamesIntegrationTests -testResults "$project/Artifacts/allgames-play.xml" -logFile "$project/Artifacts/allgames-play.log"
wait "$browser_pid"
print 'ALL_GAMES_TESTS_OK — Artifacts/allgames-play.xml and allgames-browser-result.json'
