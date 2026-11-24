#!/usr/bin/env bash
# Ad-hoc signs a macOS application bundle with Apple's codesign, so on a Mac only.
#
#   tools/release/sign-macos.sh <bundle.app>
#
# Ad-hoc, without the hardened runtime: that needs JIT entitlements for .NET and buys nothing unnotarized.
# Inside out, as Apple asks (codesign --deep is deprecated): every native library, then the bundle, which seals
# the main executable and every other file in it. package.sh signs with this on a Mac and with rcodesign on
# Linux, whose seal leaves out the non-code files beside the executable; that is enough for Apple silicon to run
# the bundle, but not for Gatekeeper to accept a quarantined copy, so release.yml re-signs the launcher with this
# before putting it in the disk image.
set -euo pipefail

APP="${1:?usage: sign-macos.sh <bundle.app>}"
[ -d "$APP/Contents" ] || { echo "error: $APP is not an application bundle" >&2; exit 2; }

find "$APP/Contents" -type f \( -name '*.dylib' -o -name '*.so' \) -print0 \
  | while IFS= read -r -d '' library; do codesign --force --sign - --timestamp=none "$library"; done
codesign --force --sign - --timestamp=none "$APP"
