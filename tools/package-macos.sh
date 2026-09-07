#!/usr/bin/env bash
# Publishes OpenIGI and wraps it in a macOS application bundle.
#
# A bare published folder does run — GLFW will open a window from it — but macOS treats a loose
# executable as a background process: it takes no focus properly, gets no Dock icon and no menu bar,
# and the window can end up behind whatever was in front. The bundle is what makes it an application.
#
# Usage: tools/package-macos.sh [osx-arm64|osx-x64] [Release|Debug]
set -euo pipefail

RID="${1:-osx-arm64}"
CONFIGURATION="${2:-Release}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="$ROOT/artifacts/macos/$RID"
APP="$STAGE/OpenIGI.app"

rm -rf "$STAGE"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj" \
  --configuration "$CONFIGURATION" \
  --runtime "$RID" \
  --self-contained true \
  --output "$APP/Contents/MacOS"

cp "$ROOT/apps/OpenIGI.Desktop/macOS/Info.plist" "$APP/Contents/Info.plist"

# The Vulkan loader is MoltenVK itself: Apple ships no Vulkan, and Silk looks for libvulkan.dylib and
# then libMoltenVK.dylib beside the executable. The package places it there through the RID's runtime
# assets, so this is a check rather than a copy — if it is missing, the app would start and then fail
# at instance creation with nothing to explain why.
if [ ! -f "$APP/Contents/MacOS/libMoltenVK.dylib" ]; then
  echo "error: libMoltenVK.dylib is not in the publish output." >&2
  echo "       Check that Silk.NET.MoltenVK.Native restored for $RID." >&2
  exit 1
fi

# Ad-hoc, which is enough to run locally and on a machine the user tells to trust it. Distribution
# needs a Developer ID signature and notarisation, neither of which belongs in a build script.
codesign --force --deep --sign - "$APP"

echo "Built $APP"
echo "Put it in the folder holding IGI.exe, or run:"
echo "  IGI_GAME_PATH=/path/to/game open -a \"$APP\""
