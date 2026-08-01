#!/usr/bin/env bash
# Captures the remaster track's reference screenshot set.
#
# These are the images `gfx_preset vanilla` is compared against for the whole of R1-R12 — the gate
# that says the modern pipeline has not changed the game as it shipped. They must be taken BEFORE
# stage R2 replaces the direct-to-backbuffer path with the deferred one; afterwards there is nothing
# left to take them from.
#
# -lockstep is what makes a capture reproducible. It does two things, both necessary:
#
#   1. Drives the simulation from a synthetic clock, one tick's worth of time per RENDERED FRAME, so
#      how long a frame took to draw stops deciding how many ticks it covered. Measured over three
#      runs each with the machine idle: two runs that happened to land on the same tick count still
#      differed by 0.05% of pixels (the cloud layers scroll on wall-clock seconds); two that landed
#      one tick apart differed by 3.86% over the whole frame. With the flag, byte-identical.
#   2. Ignores the mouse and never grabs the cursor. The window locks the cursor in player mode, so
#      moving the mouse in ANOTHER application changed what got captured — which is how the first
#      attempt at this set came out non-reproducible for a reason that had nothing to do with time.
#
# Everything else was already deterministic; IgiRandom is seeded, not time-seeded.
#
# Caveat worth knowing before trusting a hash: rasterisation is deterministic for a given GPU and
# driver, not across them. On different hardware, compare the PNGs by eye (or with a tolerance)
# rather than by hash.
#
# usage:  bash tools/capture-reference.sh [output-dir] [extra console statements...]
#         bash tools/capture-reference.sh out +gfx_preset vanilla
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/docs/remaster/reference}"
shift || true
# Anything after the output directory is passed to every capture, which is how the comparison run
# asks for `+gfx_preset vanilla` without a second copy of the view table.
EXTRA=("$@")
TMP="$ROOT/.capture-tmp"

# The game and Python are Windows programs; this script runs under Git Bash. Anything handed to
# either has to be a Windows path, or it silently writes somewhere neither can find.
win() { cygpath -w "$1" 2>/dev/null || printf '%s' "$1"; }

WIDTH=1280
HEIGHT=720
FRAMES=30

# Missions with distinct terrain and lighting. Every view starts from the mission's own spawn, so a
# position never has to be written down and can never drift.
MISSIONS=(1 3 6 9)

# name | extra arguments
#
# One walking view and three flying ones. The walking view is the only one that carries the HUD, the
# first-person arms and the held weapon, so it is what guards those; -yaw and -pitch steer the FREE
# camera only, which is why turning the player view is not a thing this set can ask for.
VIEWS=(
  "player|"
  "east|-freecam -yaw 0.4 -pitch -0.10"
  "west|-freecam -yaw 3.4 -pitch -0.10"
  "aerial|-freecam -up 120 -yaw 0.4 -pitch -0.80"
)

mkdir -p "$OUT" "$TMP"
: > "$OUT/manifest.txt"

echo "Building..."
dotnet build "$ROOT/apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj" -v q --nologo | tail -1

for mission in "${MISSIONS[@]}"; do
  for view in "${VIEWS[@]}"; do
    name="${view%%|*}"
    extra="${view#*|}"
    stem="m${mission}_${name}"
    bmp="$TMP/$stem.bmp"

    printf 'Capturing %-14s ' "$stem"
    # shellcheck disable=SC2086
    if ! dotnet run --project "$ROOT/apps/OpenIGI.Desktop" --no-build -- \
        -mission "$mission" -lockstep -size "${WIDTH}x${HEIGHT}" -frames "$FRAMES" \
        $extra "${EXTRA[@]}" -screenshot "$(win "$bmp")" > "$TMP/$stem.log" 2>&1; then
      echo "FAILED (see $TMP/$stem.log)"
      continue
    fi

    if [ ! -f "$bmp" ]; then
      echo "no capture written (see $TMP/$stem.log)"
      continue
    fi

    python -c "
from PIL import Image
Image.open(r'$(win "$bmp")').save(r'$(win "$OUT/$stem.png")', optimize=True)
"
    printf '%s  %s\n' "$(sha256sum "$bmp" | cut -d' ' -f1)" "$stem" >> "$OUT/manifest.txt"
    echo "ok"
  done
done

rm -rf "$TMP"
echo
echo "Wrote $(find "$OUT" -name '*.png' | wc -l) reference image(s) to $OUT"
