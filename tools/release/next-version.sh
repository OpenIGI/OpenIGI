#!/usr/bin/env bash
# Prints the version the next release on a channel gets, worked out from the commits since the last one.
#
#   tools/release/next-version.sh beta     ->  0.2.0-beta.3
#   tools/release/next-version.sh stable   ->  0.2.0
#
# The base version is git-cliff's bump from the last stable tag reachable from HEAD (cliff.toml: a feature
# is a minor bump before 1.0, anything else a patch). A beta is that base plus the next free -beta.N, so
# every beta of a cycle shares the version it is heading for. A stable release takes the base itself,
# which is the same number its betas carried.
set -euo pipefail

CHANNEL="${1:?usage: next-version.sh <beta|stable>}"
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

CLIFF="${GIT_CLIFF:-git-cliff}"
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
    echo "unknown channel '$CHANNEL' (beta or stable)" >&2
    exit 2
    ;;
esac
