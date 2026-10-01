#!/usr/bin/env bash
# Publishes one component of a release for one runtime and packs it into the archive players download.
#
#   tools/release/package.sh <game|launcher|web> <runtime> <version> [output-dir]
#   tools/release/package.sh game win-x64 0.2.0-beta.3
#   tools/release/package.sh launcher osx-arm64 0.2.0-beta.3
#   tools/release/package.sh web browser-wasm 0.2.0-beta.3
#
# The archive is artifacts/release/openigi-<component>-<version>-<runtime>.<zip|tar.gz>: a zip for
# Windows, a tarball everywhere else so the executable bit survives. Its contents are what the launcher
# extracts into a version folder (or, for the web build, what is served as the site root), with no
# wrapping directory. The release workflow runs exactly this, so a release can be reproduced locally.
#
# macOS gets an application bundle. A bare published folder does run, but macOS treats a loose executable
# as a background process: no focus, no Dock icon, no menu bar. The bundle is ad-hoc signed — codesign on
# a Mac, rcodesign elsewhere — because Apple silicon refuses to run unsigned code at all. Distribution
# beyond that needs a Developer ID and notarisation, which is not this script's business.
set -euo pipefail

COMPONENT="${1:?usage: package.sh <game|launcher|web> <runtime> <version> [output-dir]}"
RID="${2:?runtime identifier required}"
VERSION="${3:?version required}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${4:-$ROOT/artifacts/release}"
STAGE="$ROOT/artifacts/stage/$COMPONENT-$RID"

case "$COMPONENT" in
  game)
    PROJECT="$ROOT/apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj"
    BUNDLE="OpenIGI.app"
    PLIST="$ROOT/apps/OpenIGI.Desktop/macOS/Info.plist"
    ;;
  launcher)
    PROJECT="$ROOT/apps/OpenIGI.Launcher/OpenIGI.Launcher.csproj"
    BUNDLE="OpenIGI Launcher.app"
    PLIST="$ROOT/apps/OpenIGI.Launcher/macOS/Info.plist"
    ;;
  web)
    PROJECT="$ROOT/apps/OpenIGI.Web/OpenIGI.Web.csproj"
    [ "$RID" = "browser-wasm" ] || { echo "error: the web build's runtime is browser-wasm" >&2; exit 2; }
    ;;
  *)
    echo "error: unknown component '$COMPONENT' (game, launcher or web)" >&2
    exit 2
    ;;
esac

# Release builds carry the release's version and nothing a player has no use for: no symbols, no XML docs.
PUBLISH=(
  --configuration Release
  --nologo
  "-p:Version=$VERSION"
  -p:DebugType=none
  -p:DebugSymbols=false
  -p:PublishDocumentationFiles=false
  -p:GenerateDocumentationFile=false
)

rm -rf "$STAGE"
mkdir -p "$STAGE" "$OUT"

if [ "$COMPONENT" = "web" ]; then
  dotnet publish "$PROJECT" "${PUBLISH[@]}" --output "$STAGE/publish"
  CONTENT="$STAGE/publish/wwwroot"
else
  PUBLISH+=(--runtime "$RID" --self-contained true)

  if [[ "$RID" == osx-* ]]; then
    APP="$STAGE/$BUNDLE"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Frameworks"
    dotnet publish "$PROJECT" "${PUBLISH[@]}" --output "$APP/Contents/MacOS"

    # The bundle reports the release's version; CFBundleVersion must be numeric, so it is the core.
    CORE="${VERSION%%-*}"
    sed -e "/<key>CFBundleVersion<\/key>/{n;s|<string>[^<]*</string>|<string>$CORE</string>|;}" \
        -e "/<key>CFBundleShortVersionString<\/key>/{n;s|<string>[^<]*</string>|<string>$VERSION</string>|;}" \
        "$PLIST" > "$APP/Contents/Info.plist"

    if [ "$COMPONENT" = "game" ]; then
      # GLFW only dlopens a loader named libvulkan.1.dylib, from the working directory (which is "/" for
      # anything Finder launches) or the bundle's private frameworks; the build copies MoltenVK under that
      # name beside the executable, and in a bundle only Contents/Frameworks is ever consulted.
      for loader in libMoltenVK.dylib libvulkan.1.dylib; do
        if [ ! -f "$APP/Contents/MacOS/$loader" ]; then
          echo "error: $loader is not in the publish output; check that Silk.NET.MoltenVK.Native restored for $RID." >&2
          exit 1
        fi
      done
      mv "$APP/Contents/MacOS/libvulkan.1.dylib" "$APP/Contents/Frameworks/libvulkan.1.dylib"
    fi

    if command -v codesign >/dev/null 2>&1; then
      codesign --force --deep --sign - "$APP"
    elif command -v rcodesign >/dev/null 2>&1; then
      rcodesign sign "$APP"
    else
      echo "error: neither codesign nor rcodesign is available to ad-hoc sign $BUNDLE" >&2
      exit 1
    fi
    CONTENT="$STAGE"
  else
    dotnet publish "$PROJECT" "${PUBLISH[@]}" --output "$STAGE/publish"
    CONTENT="$STAGE/publish"
  fi
fi

if [[ "$RID" == win-* ]]; then
  ARCHIVE="$OUT/openigi-$COMPONENT-$VERSION-$RID.zip"
  rm -f "$ARCHIVE"
  if command -v zip >/dev/null 2>&1; then
    (cd "$CONTENT" && zip -qr9 "$ARCHIVE" .)
  else
    # Git Bash on Windows has no zip; Python's zipfile writes the same archive.
    (cd "$CONTENT" && python -m zipfile -c "$ARCHIVE" $(ls -A))
  fi
else
  ARCHIVE="$OUT/openigi-$COMPONENT-$VERSION-$RID.tar.gz"
  rm -f "$ARCHIVE"
  tar -C "$CONTENT" -czf "$ARCHIVE" .
fi

echo "$ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
