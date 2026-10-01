#!/usr/bin/env bash
# Writes a release's notes: the Markdown the GitHub release shows and the git-cliff context the manifest
# (and so the launcher) is built from.
#
#   tools/release/notes.sh <tag> <output-dir>     -> <output-dir>/notes.md, <output-dir>/context.json
#
# A beta lists what changed since the release before it, beta or stable. A stable release lists everything
# since the previous stable release, which is every change its betas carried. The very first release has
# no history worth listing (the whole project would be one release note), so it says that it is the first.
set -euo pipefail

TAG="${1:?usage: notes.sh <tag> <output-dir>}"
OUT="${2:?output directory required}"
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
CLIFF="${GIT_CLIFF:-git-cliff}"
mkdir -p "$OUT"

STABLE_RE='^v[0-9]+\.[0-9]+\.[0-9]+$'
ANY_RE='^v[0-9]+\.[0-9]+\.[0-9]+(-beta\.[0-9]+)?$'
if [[ "$TAG" =~ $STABLE_RE ]]; then PATTERN="$STABLE_RE"; else PATTERN="$ANY_RE"; fi

# The newest release tag behind this one, by version (betas order before their release).
PREVIOUS="$(git -c versionsort.suffix=-beta tag --merged "$TAG^" --sort=-v:refname \
  | grep -E "$PATTERN" | grep -vx "$TAG" | head -n 1 || true)"

if [ -z "$PREVIOUS" ]; then
  printf 'The first release of OpenIGI on its update channels.\n' > "$OUT/notes.md"
  printf '[]\n' > "$OUT/context.json"
  echo "first release: no previous tag" >&2
  exit 0
fi

echo "notes for $TAG: $PREVIOUS..$TAG" >&2
$CLIFF "$PREVIOUS..$TAG" --tag "$TAG" --strip all > "$OUT/notes.md"
$CLIFF "$PREVIOUS..$TAG" --tag "$TAG" --context > "$OUT/context.json"
if ! grep -q '^- ' "$OUT/notes.md"; then
  printf 'Maintenance release: no player-facing changes since %s.\n' "$PREVIOUS" > "$OUT/notes.md"
fi
