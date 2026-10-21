#!/usr/bin/env bash
# Packs what a mod's author builds against, at a game release's version, as NuGet packages.
#
#   tools/release/pack-modding-sdk.sh <version> [output-dir]
#
#   tools/release/pack-modding-sdk.sh 0.2.0-beta.3        artifacts/nuget/OpenIGI.Modding.Sdk.0.2.0-beta.3.nupkg, ...
#
# OpenIGI.Modding.Sdk is the one package a mod references: it brings the modding API and both games' gameplay and
# presentation assemblies, each its own package at the same version, and the build that turns a project with a mod.json
# into the folder the launcher installs. OpenIGI.Modding.Templates is `dotnet new openigi-mod`, referencing that version.
# A mod built against version V works with game builds from V on that still offer its modding API.
set -euo pipefail

VERSION="${1:?usage: pack-modding-sdk.sh <version> [output-dir]}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${2:-$ROOT/artifacts/nuget}"

mkdir -p "$OUT"
dotnet pack "$ROOT/sdk/OpenIGI.Modding.Sdk/OpenIGI.Modding.Sdk.csproj" -c Release -p:Version="$VERSION" -o "$OUT"
dotnet pack "$ROOT/sdk/OpenIGI.Modding.Templates/OpenIGI.Modding.Templates.csproj" -c Release -p:Version="$VERSION" -o "$OUT"
