#!/usr/bin/env bash
# The paths whose commits make a launcher release, sourced by next-version.sh and notes.sh.
#
# The launcher's own code, the release contract it reads, and the two interface libraries it is drawn with.
# Its other dependencies (Core, Input, Platform) are shared with the game and change for the game's sake;
# their commits belong in the game's notes, not the launcher's.
LAUNCHER_PATHS=(
  apps/OpenIGI.Launcher
  src/Core/OpenIGI.Releases
  src/UI/OpenIGI.UI.Kit
  src/UI/OpenIGI.UI.Skia
)

# The same paths as git-cliff include patterns.
LAUNCHER_GLOBS=()
for path in "${LAUNCHER_PATHS[@]}"; do
  LAUNCHER_GLOBS+=(--include-path "$path/**")
done

# The release a launcher release counts from: the newest launcher-v tag behind HEAD, or before the first one
# the stable release that shipped the first launcher inside it.
previous_launcher_tag() {
  local ref="${1:-HEAD}" tag
  tag="$(git tag --merged "$ref" --sort=-v:refname -l 'launcher-v*' | grep -E '^launcher-v[0-9]+\.[0-9]+\.[0-9]+$' | head -n 1 || true)"
  if [ -z "$tag" ]; then
    tag="$(git tag --merged "$ref" --sort=-v:refname -l 'v*' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | head -n 1 || true)"
  fi
  echo "$tag"
}
