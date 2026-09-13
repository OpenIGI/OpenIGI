#!/usr/bin/env bash
# Captures each viewpoint twice -- the game's own textures with no derived maps, and the upscaled
# pack with them -- so the whole remaster track can be judged against the original at once.
#
# This is the comparison r_hd (F8) and r_pbr (F9) make by hand. Both packs are LOADED in both runs
# and the cvars decide whether they are drawn, so the two frames differ only in what is sampled --
# not in what was uploaded, which would also change memory, load time and mip selection.
#
# The replacement art is whatever is in <game>/overrides, so there is nothing to point at: r_overrides
# decides whether it is drawn. -lockstep still ignores settings.cfg, which is why the preset is named.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
GAME="$ROOT/apps/OpenIGI.Desktop/bin/Release/net10.0/OpenIGI.Desktop.exe"
OUT="${1:?usage: compare-remaster.sh <out-dir> [detail-strength] [size] [frames]}"
DETAIL="${2:-1}"
SIZE="${3:-1280x720}"
FRAMES="${4:-25}"

mkdir -p "$OUT"

win() { cygpath -w "$1" 2>/dev/null || printf '%s' "$1"; }

# mission | name | pose. Every pose is a mission's own spawn eye, which the loader prints at load --
# so none of them is a magic number and none can drift as the levels change.
VIEWS=(
  "1|m1_spawn|-campos 6000.614,-13635.941,42583.133 -yaw 3.0960 -pitch 0"
  "1|m1_wall|-campos 6000.614,-13655.941,42583.133 -yaw -1.5708 -pitch 0"
  "1|m1_office|-campos 5994.614,-13655.941,42583.133 -yaw -1.5708 -pitch 0"
  "1|m1_tanks|-campos 5970.614,-13675.941,42583.133 -yaw -1.5708 -pitch 0"
  "2|m2_spawn|-campos 30793.885,-9228.685,42831.133 -yaw 3.6480 -pitch 0"
  "3|m3_spawn|-campos 5943.934,-13179.626,42648.467 -yaw -3.4558 -pitch 0"
  "5|m5_spawn|-campos -5910.566,-8510.867,44175.133 -yaw 2.5133 -pitch 0"
  "7|m7_spawn|-campos -1773.729,22254.193,42668.467 -yaw 4.2896 -pitch 0"
  "9|m9_spawn|-campos 5581.323,-12328.572,42672.771 -yaw 4.2935 -pitch 0"
)

shoot() {   # mission name pose tag hd pbr
  local bmp="$OUT/$2_$4.bmp"
  # shellcheck disable=SC2086
  "$GAME" -mission "$1" -lockstep -skipcutscene -freecam -size "$SIZE" -frames "$FRAMES" \
    $3 -screenshot "$(win "$bmp")" \
    +gfx_preset ultra +r_material_detail "$DETAIL" +r_overrides "$5" +r_pbr "$6" \
    >"$OUT/$2_$4.log" 2>&1
  python -c "
from PIL import Image
Image.open(r'$(win "$bmp")').save(r'$(win "${bmp%.bmp}.png")')"
  rm -f "$bmp"
}

for view in "${VIEWS[@]}"; do
  IFS='|' read -r mission name pose <<< "$view"
  printf 'Capturing %-12s ' "$name"
  shoot "$mission" "$name" "$pose" original 0 0
  shoot "$mission" "$name" "$pose" remastered 1 1
  echo ok
done

echo
echo "Wrote $(find "$OUT" -name '*.png' | wc -l) image(s) to $OUT"
