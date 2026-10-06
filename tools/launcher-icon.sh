#!/usr/bin/env bash
# Makes the launcher's icon from the game's: the same emblem, its green radar turned the launcher's amber.
#
#   tools/launcher-icon.sh            (ImageMagick 7 on the path as `magick`)
#
# Reads  src/Platform/OpenIGI.Platform.Desktop/Assets/openigi.ico
# Writes src/Platform/OpenIGI.Platform.Desktop/Assets/openigi-launcher.ico
#
# Each size the game's icon carries is recoloured on its own, so the small ones keep their own detail rather
# than being shrunk from the largest. Per frame:
#   - steel:  the emblem with its colour taken out, so the metal is neutral rather than faintly green;
#   - amber:  its brightness mapped black -> the launcher's accent rgb(232,163,61) -> a pale amber highlight,
#             lifted a little (gamma 1.35) so the dark radar still reads warm at taskbar sizes;
#   - mask:   how much greener than red or blue each pixel is, strongly amplified: the radar, the crosshair
#             glints and the lit rings, and none of the metal.
# Steel and amber are mixed through the mask, and the original's transparency is put back.
set -euo pipefail

# Relative paths from the repository's root throughout: Git Bash's /d/... spelling means nothing to a Windows
# magick.exe, and a relative path means the same to both.
cd "$(dirname "$0")/.."
source="src/Platform/OpenIGI.Platform.Desktop/Assets/openigi.ico"
target="src/Platform/OpenIGI.Platform.Desktop/Assets/openigi-launcher.ico"
work="$(mktemp -d ./.launcher-icon.XXXXXX)"
trap 'rm -rf "$work"' EXIT

magick xc:black "xc:rgb(232,163,61)" "xc:rgb(255,226,170)" +append -filter triangle -resize 256x1! "$work/clut.png"

frames=()
count="$(magick identify "$source" | wc -l)"
for ((i = 0; i < count; i++)); do
  frame="$work/frame-$i.png"
  magick "$source[$i]" "$frame"
  magick "$frame" -alpha off -modulate 100,0 -type TrueColor -define png:color-type=2 "$work/steel-$i.png"
  magick "$frame" -alpha off -colorspace gray -gamma 1.35 "$work/clut.png" -clut "$work/amber-$i.png"
  magick "$frame" -alpha off -fx "min(1,max(0,(g-max(r,b))*22))" -colorspace gray "$work/mask-$i.png"
  magick "$work/steel-$i.png" "$work/amber-$i.png" "$work/mask-$i.png" -composite \
    \( "$frame" -alpha extract \) -alpha off -compose CopyOpacity -composite \
    -type TrueColorAlpha -define png:color-type=6 "$work/out-$i.png"
  frames+=("$work/out-$i.png")
done

magick "${frames[@]}" "$target"
magick identify "$target"
