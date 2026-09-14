#!/usr/bin/env bash
# Repeatable, non-mutating Level 6 vehicle/fuel evidence and focused-regression gate.
# Run only from the PR code worktree. Desktop rendering belongs to the companion
# verify-level6-vehicle-playtest.sh script in a local test/*-macos-build worktree.
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"

branch=$(git branch --show-current)
case "$branch" in
  test/*-macos-build|macos-build)
    echo "error: static verification must run in the PR code worktree, not $branch" >&2
    exit 2
    ;;
esac

game_path=${IGI_GAME_PATH:-/Users/haseeb-mir/Downloads/IGI}
converter=${IGI1CONV:-/private/tmp/project-igi-converter-cli-build/bin/igi1conv}
artifact_dir=${LEVEL6_EVIDENCE_DIR:-/private/tmp/openigi-level6-vehicle-evidence}
igi_exe="$game_path/IGI.EXE"
level_root="$game_path/MISSIONS/location0/LEVEL6"

command -v dotnet >/dev/null || { echo "error: dotnet is required" >&2; exit 1; }
command -v r2 >/dev/null || { echo "error: r2 is required" >&2; exit 1; }
[ -x "$converter" ] || { echo "error: igi1conv not found: $converter" >&2; exit 1; }
[ -f "$igi_exe" ] || { echo "error: retail IGI.EXE not found: $igi_exe" >&2; exit 1; }
[ -f "$level_root/OBJECTS.QVM" ] || { echo "error: Level 6 OBJECTS.QVM not found" >&2; exit 1; }

mkdir -p "$artifact_dir"
qsc="$artifact_dir/level6-objects.qsc"
qas="$artifact_dir/level6-objects.qas"
apc_qsc="$artifact_dir/apc-weapon.qsc"
apc_qas="$artifact_dir/apc-weapon.qas"
"$converter" qvm decompile "$level_root/OBJECTS.QVM" -o "$qsc"
"$converter" qvm disasm "$level_root/OBJECTS.QVM" -o "$qas"
"$converter" qvm decompile "$game_path/WEAPONS/APC/WEAPON.QVM" -o "$apc_qsc"
"$converter" qvm disasm "$game_path/WEAPONS/APC/WEAPON.QVM" -o "$apc_qas"

# The QSC assertions protect authored facts; QAS is retained beside it for control-flow review.
rg -F 'Task_New(800, "Car", "APC"' "$qsc" | rg -F '"1", "", 180.0, 360.0, 50.0' >/dev/null
rg -F 'Task_New(803, "Car"' "$qsc" | rg -F '"", "", 180.0, 360.0, 30.0' >/dev/null
rg -F 'Task_New(801, "Car", "Priboi' "$qsc" | rg -F '"", "", 180.0, 360.0, 30.0' >/dev/null
rg -F 'PlaceExplosiveTBA' "$qsc" >/dev/null
rg -F 'TASKTYPE_MISSILE' "$apc_qsc" >/dev/null
if command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$igi_exe" "$qsc" "$qas" "$apc_qsc" "$apc_qas" > "$artifact_dir/SHA256SUMS"
else
  shasum -a 256 "$igi_exe" "$qsc" "$qas" "$apc_qsc" "$apc_qas" > "$artifact_dir/SHA256SUMS"
fi

# Capture disassembly, xrefs and discovered function metadata. This does not assert semantics:
# Vehicle fire/contact must still pass the dual-decompiler + live-capture gate before translation.
r2 -q -e bin.cache=true -c 'aaa; af @ 0x425de0; af @ 0x42eca0; af @ 0x4fb9a0; afl~0x00425d; afl~0x0042ec; afl~0x004fb9; axt @ 0x4fb9a0; pdf @ 0x425de0; pdf @ 0x42eca0; pdf @ 0x4fb9a0; q' "$igi_exe" \
  > "$artifact_dir/r2-vehicle-paths.txt"

IGI_GAME_PATH="$game_path" dotnet test tests/OpenIGI.Game.Tests/OpenIGI.Game.Tests.csproj \
  --filter 'FullyQualifiedName~Issue105Tests|FullyQualifiedName~CarDynamicsTests' --nologo

echo "Level 6 static gate passed. Evidence: $artifact_dir"
