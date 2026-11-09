#!/usr/bin/env bash
# Fetches the prebuilt libdatachannel for each runtime named into third-party/libdatachannel/<rid>/, where
# DataChannelNative.props copies it beside the game. Built by .github/workflows/native-datachannel.yml.
#
#   tools/native/fetch-datachannel.sh win-x64 linux-x64 osx-arm64
set -euo pipefail

VERSION=0.23.2
REPOSITORY=${GITHUB_REPOSITORY:-OpenIGI/OpenIGI}
RELEASE=natives-libdatachannel-$VERSION
ROOT=$(cd "$(dirname "$0")/../.." && pwd)

for rid in "$@"; do
  destination="$ROOT/third-party/libdatachannel/$rid"
  mkdir -p "$destination"
  file=libdatachannel-$rid.tar.gz
  # Downloaded to a file first, so a try that fails halfway never feeds tar a broken stream. GitHub's download
  # address can answer 504 for half an hour from some networks while its API serves the same file, so a failing
  # address falls back to the API (gh, signed in on a workstation, GH_TOKEN on a runner).
  archive=$(mktemp)
  if ! curl -fsSL --retry 4 --retry-all-errors --retry-delay 10 -o "$archive" \
      "https://github.com/$REPOSITORY/releases/download/$RELEASE/$file"; then
    echo "download address failed for $file; fetching it through the GitHub API" >&2
    gh release download "$RELEASE" --repo "$REPOSITORY" --pattern "$file" --output "$archive" --clobber
  fi
  tar -xzf "$archive" -C "$destination"
  rm -f "$archive"
  echo "libdatachannel $VERSION -> $destination"
done
