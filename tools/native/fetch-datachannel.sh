#!/usr/bin/env bash
# Fetches the prebuilt libdatachannel for each runtime named into third-party/libdatachannel/<rid>/, where
# DataChannelNative.props copies it beside the game. Built by .github/workflows/native-datachannel.yml.
#
#   tools/native/fetch-datachannel.sh win-x64 linux-x64 osx-arm64
set -euo pipefail

VERSION=0.23.2
REPOSITORY=${GITHUB_REPOSITORY:-OpenIGI/OpenIGI}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)

for rid in "$@"; do
  destination="$ROOT/third-party/libdatachannel/$rid"
  mkdir -p "$destination"
  # GitHub's download servers can answer 504 for minutes at a time; later tries often reach a working one.
  # Downloaded to a file first, so a try that fails halfway never feeds tar a broken stream.
  archive=$(mktemp)
  curl -fsSL --retry 10 --retry-all-errors --retry-delay 20 -o "$archive" \
    "https://github.com/$REPOSITORY/releases/download/natives-libdatachannel-$VERSION/libdatachannel-$rid.tar.gz"
  tar -xzf "$archive" -C "$destination"
  rm -f "$archive"
  echo "libdatachannel $VERSION -> $destination"
done
