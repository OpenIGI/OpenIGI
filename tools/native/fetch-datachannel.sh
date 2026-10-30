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
  curl -fsSL "https://github.com/$REPOSITORY/releases/download/natives-libdatachannel-$VERSION/libdatachannel-$rid.tar.gz" \
    | tar -xz -C "$destination"
  echo "libdatachannel $VERSION -> $destination"
done
