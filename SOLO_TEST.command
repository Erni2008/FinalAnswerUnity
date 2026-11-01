#!/bin/zsh
set -eu
project="$(cd "$(dirname "$0")" && pwd)"
exec "$project/Tools/run-manual-qa.sh"
