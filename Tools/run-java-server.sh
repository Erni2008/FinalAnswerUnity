#!/bin/zsh
set -eu
java_project="$(cd "$(dirname "$0")/../../FinalAnswer" && pwd)"
if curl -fsS http://127.0.0.1:18096/health >/dev/null 2>&1; then
  if ! curl -fsS http://127.0.0.1:18096/ >/dev/null 2>&1; then
    "$java_project/gradlew" -p "$java_project" :server:processResources --offline
    curl -fsS http://127.0.0.1:18096/ >/dev/null
  fi
  print 'Java API and browser controllers respond on http://127.0.0.1:18096' 
  exit 0
fi
exec "$java_project/gradlew" -p "$java_project" :server:lastCommandDev -Plc.port=18096 --offline --console=plain
