#!/usr/bin/env pwsh
# Fetches the wgpu-native release the WebGPU bindings were generated against.
#
#   pwsh tools/fetch-wgpu-native.ps1              this machine's platform
#   pwsh tools/fetch-wgpu-native.ps1 -All         every platform, for a publish
#   pwsh tools/fetch-wgpu-native.ps1 -Force       re-download over what is there
#
# The binary is another project's redistributable and this repository is GPL-3.0, so it is fetched
# rather than carried: it lands in the gitignored third-party/, which is one of the places
# WgpuLibrary looks. The release is read out of the bindings' own VERSION file, so the library loaded
# and the headers bound against cannot drift apart -- a mismatch there does not fail at load, it fails
# in the middle of a frame at a structure whose layout has moved.

[CmdletBinding()]
param(
    [switch]$All,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$native = Join-Path $root 'src/OpenIGI.Rendering.WebGpu.Native/native'
$destination = Join-Path $root 'third-party/wgpu-native'

$version = (Get-Content (Join-Path $native 'VERSION') -Raw).Trim()
if (-not $version) {
    throw "No release named in $native/VERSION."
}

Write-Host "wgpu-native $version"

# What each runtime identifier is called in the release's own asset names, and what the library file is
# called once it is unpacked.
$platforms = @(
    @{ Rid = 'win-x64';     Asset = 'windows-x86_64-msvc';  File = 'wgpu_native.dll' }
    @{ Rid = 'win-arm64';   Asset = 'windows-aarch64-msvc'; File = 'wgpu_native.dll' }
    @{ Rid = 'linux-x64';   Asset = 'linux-x86_64';         File = 'libwgpu_native.so' }
    @{ Rid = 'linux-arm64'; Asset = 'linux-aarch64';        File = 'libwgpu_native.so' }
    @{ Rid = 'osx-x64';     Asset = 'macos-x86_64';         File = 'libwgpu_native.dylib' }
    @{ Rid = 'osx-arm64';   Asset = 'macos-aarch64';        File = 'libwgpu_native.dylib' }
)

if (-not $All) {
    $os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        'X64' { 'x64' }
        'Arm64' { 'arm64' }
        default { throw "No wgpu-native release for $_." }
    }

    $rid = "$os-$architecture"
    $platforms = $platforms | Where-Object { $_.Rid -eq $rid }
    if (-not $platforms) {
        throw "No wgpu-native release for $rid."
    }
}

foreach ($platform in $platforms) {
    $folder = Join-Path $destination $platform.Rid
    $library = Join-Path $folder $platform.File

    if ((Test-Path $library) -and -not $Force) {
        Write-Host "  have      $($platform.Rid)"
        continue
    }

    # The release ships a debug build beside the release one; the name without a qualifier is release.
    $asset = "wgpu-$($platform.Asset)-release.zip"
    $url = "https://github.com/gfx-rs/wgpu-native/releases/download/$version/$asset"
    $archive = Join-Path ([System.IO.Path]::GetTempPath()) $asset

    Write-Host "  fetching  $($platform.Rid) <- $asset"
    Invoke-WebRequest -Uri $url -OutFile $archive

    $unpacked = Join-Path ([System.IO.Path]::GetTempPath()) "wgpu-$($platform.Rid)-$([guid]::NewGuid().ToString('N'))"
    Expand-Archive -Path $archive -DestinationPath $unpacked -Force

    $found = Get-ChildItem -Path $unpacked -Recurse -Filter $platform.File | Select-Object -First 1
    if (-not $found) {
        throw "$asset holds no $($platform.File)."
    }

    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    Copy-Item -Path $found.FullName -Destination $library -Force

    # The headers this release ships are what the bindings must have been generated from. Compared
    # rather than trusted, because a release whose headers moved is exactly the case this guards.
    $shipped = Get-ChildItem -Path $unpacked -Recurse -Filter 'webgpu.h' | Select-Object -First 1
    if ($shipped) {
        $theirs = (Get-Content $shipped.FullName -Raw) -replace "`r`n", "`n"
        $ours = (Get-Content (Join-Path $native 'webgpu.h') -Raw) -replace "`r`n", "`n"
        if ($theirs -ne $ours) {
            Write-Warning "$asset ships a webgpu.h that differs from the committed one. Replace the headers in $native and regenerate the bindings."
        }
    }

    Remove-Item $archive -Force
    Remove-Item $unpacked -Recurse -Force
    Write-Host "  fetched   $library"
}

Write-Host ''
Write-Host "Set OPENIGI_WGPU_NATIVE to load one from somewhere else instead."
