#!/usr/bin/env bash
# Re-captures the reference set and reports how far the current build has drifted from it.
#
# This is the remaster track's regression gate: with `gfx_preset vanilla` the answer must stay
# "identical". Run it after any stage that touches the render path.
#
# usage:  bash tools/compare-reference.sh [extra console statements...]
#         bash tools/compare-reference.sh +gfx_preset vanilla
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REF="$ROOT/docs/remaster/reference"
NEW="$ROOT/.capture-compare"

if [ ! -f "$REF/manifest.txt" ]; then
  echo "No reference set at $REF. Run tools/capture-reference.sh first." >&2
  exit 1
fi

rm -rf "$NEW"
bash "$ROOT/tools/capture-reference.sh" "$NEW" "$@" > /dev/null

win() { cygpath -w "$1" 2>/dev/null || printf '%s' "$1"; }

status=0
printf '%-14s %s\n' "VIEW" "RESULT"

while read -r _ stem; do
  [ -n "$stem" ] || continue

  if [ ! -f "$NEW/$stem.png" ]; then
    printf '%-14s %s\n' "$stem" "MISSING — the capture did not run"
    status=1
    continue
  fi

  if ! python - "$(win "$REF/$stem.png")" "$(win "$NEW/$stem.png")" <<'PY'
import sys
from PIL import Image, ImageChops

a = Image.open(sys.argv[1]).convert("RGB")
b = Image.open(sys.argv[2]).convert("RGB")
name = sys.argv[1].replace("\\", "/").rsplit("/", 1)[-1][:-4]

if a.size != b.size:
    print(f"{name:<14} SIZE CHANGED {a.size} -> {b.size}")
    raise SystemExit(2)

diff = ImageChops.difference(a, b)
box = diff.getbbox()
if box is None:
    print(f"{name:<14} identical")
    raise SystemExit(0)

changed = sum(1 for p in diff.getdata() if p != (0, 0, 0))
worst = max(max(p) for p in diff.getdata())
total = a.size[0] * a.size[1]
print(f"{name:<14} DIFFERS  {100 * changed / total:.2f}% of pixels, worst channel delta {worst}, bbox {box}")
raise SystemExit(2)
PY
  then
    status=1
  fi
done < "$REF/manifest.txt"

echo
if [ $status -eq 0 ]; then
  echo "No drift. The vanilla path still reproduces the reference set."
else
  echo "Drift found. Compare $NEW against $REF."
  echo "If the change is intended, say so explicitly — do not silently re-capture the reference."
fi
exit $status
