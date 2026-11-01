#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PLATFORM=mac
YAK="${AHOLO_YAK_PATH:-/Applications/Rhino 8.app/Contents/Resources/bin/yak}"
BUILD=1
while [ "$#" -gt 0 ]; do
  case "$1" in
    --platform) PLATFORM="$2"; shift 2 ;;
    --yak) YAK="$2"; shift 2 ;;
    --no-build) BUILD=0; shift ;;
    --help) echo 'Usage: build-package.sh [--platform mac|win] [--yak PATH] [--no-build]'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
case "$PLATFORM" in mac|win) ;; *) echo 'Platform must be mac or win.' >&2; exit 2 ;; esac
[ -x "$YAK" ] || { echo 'Provide the Rhino Yak CLI with --yak.' >&2; exit 1; }
cd "$ROOT"
CHECKS=not-rerun
if [ "$BUILD" -eq 1 ]; then
  dotnet build Aholo.sln -c Release
  dotnet test tests/Aholo.Tests/Aholo.Tests.csproj -c Release --no-build --no-restore
  CHECKS=passed
fi
python3 scripts/package.py --platform "$PLATFORM" --yak "$YAK" --checks "$CHECKS"
