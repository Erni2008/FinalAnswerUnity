#!/bin/zsh
set -eu
project="$(cd "$(dirname "$0")/.." && pwd)"
editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
exec "$editor" -projectPath "$project" -logFile "$project/Logs/Editor.log"
