#!/usr/bin/env bash
# Prints the version the next release on a channel gets, worked out from the commits since the last one.
#
#   tools/release/next-version.sh beta       ->  0.2.0-beta.3     (the game, from develop)
#   tools/release/next-version.sh stable     ->  0.2.0            (the game, from master)
#   tools/release/next-version.sh launcher   ->  0.1.1            (the launcher alone, from master)
#
# A game release's base version is git-cliff's bump from the last stable game tag reachable from HEAD
# (cliff.toml: a feature is a minor bump before 1.0, anything else a patch). A beta is that base plus the
# next free -beta.N, so every beta of a cycle shares the version it is heading for. A stable release takes
# the base itself, which is the same number its betas carried.
#
# The launcher has its own line of launcher-v tags, counted from the last launcher release (or from v0.1.0,
# which shipped the first launcher): a minor bump if any launcher commit since is a feature, else a patch.
set -euo pipefail

CHANNEL="${1:?usage: next-version.sh <beta|stable|launcher>}"
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
# shellcheck source=launcher-paths.sh
. tools/release/launcher-paths.sh

CLIFF="${GIT_CLIFF:-git-cliff}"

if [ "$CHANNEL" = "launcher" ]; then
  PREVIOUS="$(previous_launcher_tag)"
  [ -n "$PREVIOUS" ] || { echo "no stable release has shipped a launcher yet" >&2; exit 1; }
  BASE="${PREVIOUS#launcher-v}"
  BASE="${BASE#v}"
  SUBJECTS="$(git log --format=%s "$PREVIOUS..HEAD" -- "${LAUNCHER_PATHS[@]}")"
  if [ -z "$SUBJECTS" ]; then
    echo "nothing has changed in the launcher since $PREVIOUS" >&2
    exit 1
  fi
  IFS=. read -r MAJOR MINOR PATCH <<< "$BASE"
  if grep -qE '^feat(\([^)]*\))?!?:' <<< "$SUBJECTS"; then
    echo "$MAJOR.$((MINOR + 1)).0"
  else
    echo "$MAJOR.$MINOR.$((PATCH + 1))"
  fi
  exit 0
fi

BASE="$($CLIFF --bumped-version 2>/dev/null)"
BASE="${BASE#v}"
[[ "$BASE" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "git-cliff gave '$BASE' as the next version" >&2; exit 1; }

case "$CHANNEL" in
  beta)
    LAST="$(git tag -l "v$BASE-beta.*" | sed -E 's/.*-beta\.//' | sort -n | tail -n 1)"
    echo "$BASE-beta.$(( ${LAST:-0} + 1 ))"
    ;;
  stable)
    if git rev-parse -q --verify "refs/tags/v$BASE" >/dev/null; then
      echo "v$BASE is already released and nothing since would bump it" >&2
      exit 1
    fi
    echo "$BASE"
    ;;
  *)
    echo "unknown channel '$CHANNEL' (beta, stable or launcher)" >&2
    exit 2
    ;;
esac
