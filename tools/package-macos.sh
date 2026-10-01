#!/usr/bin/env bash
# Builds OpenIGI as a macOS application bundle for running locally.
#
# Usage: tools/package-macos.sh [osx-arm64|osx-x64] [version]
#
# The bundle is made by tools/release/package.sh, the same script a release runs, so what runs here is
# what players get; see there for why it is a bundle and how it is signed. The bundle is left at
# artifacts/stage/game-<rid>/OpenIGI.app, beside the release archive in artifacts/release.
set -euo pipefail

RID="${1:-osx-arm64}"
VERSION="${2:-0.0.0-dev}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

"$ROOT/tools/release/package.sh" game "$RID" "$VERSION"

APP="$ROOT/artifacts/stage/game-$RID/OpenIGI.app"
echo "Built $APP"
echo "Put it in the folder holding IGI.exe, or run:"
echo "  IGI_GAME_PATH=/path/to/game open -a \"$APP\""
