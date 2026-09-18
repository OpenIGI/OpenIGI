#!/usr/bin/env bash
# Release desktop boot/render gate for Level 6. This intentionally does not claim
# combat fidelity: a natural retail Windows x86 probe capture is required for that.
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"

branch=$(git branch --show-current)
case "$branch" in
  test/*-macos-build) ;;
  *)
    echo "error: playtest verification must run in a local test/*-macos-build worktree, not $branch" >&2
    exit 2
    ;;
esac

game_path=${IGI_GAME_PATH:-/Users/haseeb-mir/Downloads/IGI}
out_dir=${LEVEL6_PLAYTEST_DIR:-"$repo_root/scratch/level6-vehicle"}
[ -d "$game_path" ] || { echo "error: IGI_GAME_PATH must point to retail game data" >&2; exit 1; }
mkdir -p "$out_dir"
capture="$out_dir/mission-6.bmp"
rm -f "$capture"

dotnet build apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj -c Release --nologo
dotnet run --project apps/OpenIGI.Desktop -c Release --no-build -- \
  -game "$game_path" -mission 6 -skipcutscene -lockstep -window -size 1280x720 \
  -frames 45 -screenshot "$capture"
[ -s "$capture" ] || { echo "error: missing or empty Level 6 screenshot: $capture" >&2; exit 1; }

echo "Level 6 Release render gate passed: $capture"
