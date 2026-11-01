#!/bin/zsh
set -eu
project="$(cd "$(dirname "$0")/.." && pwd)"
exec node "$project/Tools/launch-manual-qa.cjs"
