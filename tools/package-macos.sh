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
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Frameworks"

dotnet publish "$ROOT/apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj" \
  --configuration "$CONFIGURATION" \
  --runtime "$RID" \
  --self-contained true \
  --output "$APP/Contents/MacOS"

cp "$ROOT/apps/OpenIGI.Desktop/macOS/Info.plist" "$APP/Contents/Info.plist"

# The Vulkan loader is MoltenVK itself: Apple ships no Vulkan. Two names are wanted rather than one.
# Silk's own loader looks for libMoltenVK.dylib, which arrives here through the RID's runtime assets;
# GLFW will only dlopen a file named libvulkan.1.dylib and refuses to make a Vulkan window without one,
# which the app's build copies alongside it. Both are checks rather than copies — a missing one is a
# failure at window creation or at instance creation with nothing in it to say what is absent.
for loader in libMoltenVK.dylib libvulkan.1.dylib; do
  if [ ! -f "$APP/Contents/MacOS/$loader" ]; then
    echo "error: $loader is not in the publish output." >&2
    echo "       Check that Silk.NET.MoltenVK.Native restored for $RID." >&2
    exit 1
  fi
done

# GLFW asks for its loader in two places and neither is this one. It dlopens the bare name
# libvulkan.1.dylib, which finds a copy in the working directory — the folder itself, for a published
# folder run in place, but "/" for anything Finder or the Dock launches — and then falls back to asking
# the main bundle for its private frameworks directory, which is Contents/Frameworks. So the copy the
# build puts beside the executable is moved there: in a bundle it is the only one of the two that is
# ever consulted, and leaving it in MacOS as well is twelve megabytes nothing would open.
mv "$APP/Contents/MacOS/libvulkan.1.dylib" "$APP/Contents/Frameworks/libvulkan.1.dylib"

# Ad-hoc, which is enough to run locally and on a machine the user tells to trust it. Distribution
# needs a Developer ID signature and notarisation, neither of which belongs in a build script.
codesign --force --deep --sign - "$APP"

echo "Built $APP"
echo "Put it in the folder holding IGI.exe, or run:"
echo "  IGI_GAME_PATH=/path/to/game open -a \"$APP\""
