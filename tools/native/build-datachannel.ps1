#!/usr/bin/env pwsh
# Builds libdatachannel, the WebRTC data channels co-op through a room plays over, into third-party/libdatachannel/<rid>/.
#
#   pwsh tools/native/build-datachannel.ps1 -Vcpkg D:/vcpkg                    this machine's platform
#   pwsh tools/native/build-datachannel.ps1 -Vcpkg ~/vcpkg -Architecture x64   macOS on Intel, from either Mac
#
# Windows (datachannel.dll), Linux (libdatachannel.so) and macOS (libdatachannel.dylib). One library with everything it
# needs linked in (OpenSSL, usrsctp, libjuice and, on Windows, the C runtime): the
# triplets beside this script say so. It is another project's (MPL-2.0), so like the other natives it is built or
# fetched into the gitignored third-party/ rather than carried, and DataChannelNative.props copies it beside the game.

[CmdletBinding()]
param(
    [string]$Vcpkg = $env:VCPKG_ROOT,
    [ValidateSet('', 'arm64', 'x64')]
    [string]$Architecture = ''
)

$ErrorActionPreference = 'Stop'

if (-not $Vcpkg -or -not (Test-Path (Join-Path $Vcpkg 'vcpkg*'))) {
    throw 'Point -Vcpkg (or VCPKG_ROOT) at a vcpkg checkout.'
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$triplets = Join-Path $PSScriptRoot 'triplets'
$installed = Join-Path ([System.IO.Path]::GetTempPath()) 'openigi-vcpkg'
if ($IsWindows) {
    $rid = 'win-x64'; $triplet = 'x64-windows-openigi'; $file = 'datachannel.dll'; $folder = 'bin'
} elseif ($IsLinux) {
    $rid = 'linux-x64'; $triplet = 'x64-linux-openigi'; $file = 'libdatachannel.so'; $folder = 'lib'
} elseif ($IsMacOS) {
    # Apple silicon by default; -Architecture x64 for an Intel build (cross-built fine on either).
    $arch = if ($Architecture) { $Architecture } elseif ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
    $rid = "osx-$arch"; $triplet = "$arch-osx-openigi"; $file = 'libdatachannel.dylib'; $folder = 'lib'
} else {
    throw 'No triplet for this platform yet.'
}

& (Join-Path $Vcpkg 'vcpkg') install "libdatachannel[core]:$triplet" "--overlay-triplets=$triplets" "--x-install-root=$installed"
if ($LASTEXITCODE -ne 0) {
    throw "vcpkg failed ($LASTEXITCODE)."
}

$built = Get-ChildItem (Join-Path $installed "$triplet/$folder") -Filter "$file*" | Sort-Object Length -Descending | Select-Object -First 1
$destination = Join-Path $root "third-party/libdatachannel/$rid"
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item $built.FullName (Join-Path $destination $file) -Force
Copy-Item (Join-Path $installed "$triplet/share/libdatachannel/copyright") (Join-Path $destination 'LICENSE') -Force
Write-Host "libdatachannel -> $destination"
