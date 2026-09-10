#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
game_path=${IGI_GAME_PATH:-}
out_dir=${E2E_OUTPUT_DIR:-"$repo_root/scratch/mvp-e2e"}
[ -n "$game_path" ] && [ -d "$game_path" ] || { echo "error: IGI_GAME_PATH must point to a retail install" >&2; exit 1; }
command -v dotnet >/dev/null || { echo "error: dotnet is required" >&2; exit 1; }
mkdir -p "$out_dir"
dotnet build "$repo_root/apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj" -c Release --nologo
for mission in 1 2 3 4 5; do
  capture="$out_dir/mission-${mission}.bmp"
  dotnet run --project "$repo_root/apps/OpenIGI.Desktop" -c Release --no-build -- \
    -game "$game_path" -mission "$mission" -skipcutscene -lockstep -window -size 1280x720 \
    -frames 45 -screenshot "$capture"
  [ -s "$capture" ] || { echo "error: missing or empty capture: $capture" >&2; exit 1; }
done
