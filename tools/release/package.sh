#!/usr/bin/env bash
# Publishes one component of a release for one runtime, as players download it.
#
#   tools/release/package.sh <game|launcher> <runtime> <version> [self-contained|dotnet] [output-dir]
#   tools/release/package.sh web browser-wasm <version> [output-dir]
#
#   tools/release/package.sh game win-x64 0.2.0-beta.3                 openigi-game-0.2.0-beta.3-win-x64.exe
#   tools/release/package.sh game win-x64 0.2.0-beta.3 dotnet          openigi-game-0.2.0-beta.3-win-x64-dotnet.exe
#   tools/release/package.sh launcher linux-x64 0.2.0-beta.3           openigi-launcher-0.2.0-beta.3-linux-x64
#   tools/release/package.sh game osx-arm64 0.2.0-beta.3               openigi-game-0.2.0-beta.3-osx-arm64.tar.gz
#
# Two flavours of every desktop build. Self-contained carries its own .NET runtime and runs on a bare
# machine; "dotnet" is the same program without it, for players who already have the .NET 10 runtime and
# would rather not download it again. The launcher installs the self-contained one.
#
# Windows and Linux get one executable file: every assembly, native library and asset is inside it, and
# the assets are unpacked to a per-user cache on first start (the game writes nothing beside itself; its
# settings, saves and caches go to the per-user folders in SettingsPaths). macOS gets an application
# bundle instead, which is what a Mac user handles as one thing, and which is also the only place GLFW
# finds the Vulkan loader. A loose executable on macOS is treated as a background process; the bundle is
# ad-hoc signed (codesign on a Mac, rcodesign elsewhere), because Apple silicon runs nothing unsigned.
#
# The web build is not a download: it is packed into artifacts/web for the site deploy.
set -euo pipefail

COMPONENT="${1:?usage: package.sh <game|launcher|web> <runtime> <version> [self-contained|dotnet] [output-dir]}"
RID="${2:?runtime identifier required}"
VERSION="${3:?version required}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

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

if [ "$COMPONENT" = "web" ]; then
  [ "$RID" = "browser-wasm" ] || { echo "error: the web build's runtime is browser-wasm" >&2; exit 2; }
  OUT="${4:-$ROOT/artifacts/web}"
  STAGE="$ROOT/artifacts/stage/web"
  rm -rf "$STAGE" && mkdir -p "$STAGE" "$OUT"
  dotnet publish "$ROOT/apps/OpenIGI.Web/OpenIGI.Web.csproj" "${PUBLISH[@]}" --output "$STAGE/publish"
  ARCHIVE="$OUT/openigi-web-$VERSION.tar.gz"
  tar -C "$STAGE/publish/wwwroot" -czf "$ARCHIVE" .
  echo "$ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
  exit 0
fi

FLAVOR="${4:-self-contained}"
OUT="${5:-$ROOT/artifacts/release}"
case "$COMPONENT" in
  game)
    PROJECT="$ROOT/apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj"
    EXECUTABLE="OpenIGI.Desktop"
    BUNDLE="OpenIGI.app"
    PLIST="$ROOT/apps/OpenIGI.Desktop/macOS/Info.plist"
    ;;
  launcher)
    PROJECT="$ROOT/apps/OpenIGI.Launcher/OpenIGI.Launcher.csproj"
    EXECUTABLE="OpenIGI.Launcher"
    BUNDLE="OpenIGI Launcher.app"
    PLIST="$ROOT/apps/OpenIGI.Launcher/macOS/Info.plist"
    ;;
  *)
    echo "error: unknown component '$COMPONENT' (game, launcher or web)" >&2
    exit 2
    ;;
esac

case "$FLAVOR" in
  self-contained) SUFFIX=""; PUBLISH+=(--runtime "$RID" --self-contained true) ;;
  dotnet) SUFFIX="-dotnet"; PUBLISH+=(--runtime "$RID" --self-contained false) ;;
  *) echo "error: unknown flavour '$FLAVOR' (self-contained or dotnet)" >&2; exit 2 ;;
esac

NAME="openigi-$COMPONENT-$VERSION-$RID$SUFFIX"
STAGE="$ROOT/artifacts/stage/$COMPONENT-$RID$SUFFIX"
rm -rf "$STAGE"
mkdir -p "$STAGE" "$OUT"

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

  ARCHIVE="$OUT/$NAME.tar.gz"
  rm -f "$ARCHIVE"
  tar -C "$STAGE" -czf "$ARCHIVE" "$BUNDLE"
else
  PUBLISH+=(
    -p:PublishSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
    -p:IncludeAllContentForSelfExtract=true
  )
  # Compression halves a self-contained file at a fraction of a second's start-up; a framework-dependent one
  # is mostly the game's own assemblies and cannot be compressed (the SDK only allows it with the runtime in).
  [ "$FLAVOR" = "self-contained" ] && PUBLISH+=(-p:EnableCompressionInSingleFile=true)

  dotnet publish "$PROJECT" "${PUBLISH[@]}" --output "$STAGE/publish"

  EXT=""
  [[ "$RID" == win-* ]] && EXT=".exe"
  FILE="$STAGE/publish/$EXECUTABLE$EXT"
  [ -f "$FILE" ] || { echo "error: the single-file publish left no $EXECUTABLE$EXT" >&2; exit 1; }
  if [ "$(find "$STAGE/publish" -type f | wc -l)" -ne 1 ]; then
    echo "error: the single-file publish left more than one file:" >&2
    find "$STAGE/publish" -type f >&2
    exit 1
  fi

  ARCHIVE="$OUT/$NAME$EXT"
  cp "$FILE" "$ARCHIVE"
  chmod 755 "$ARCHIVE"
fi

echo "$ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
