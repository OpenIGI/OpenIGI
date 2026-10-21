#!/usr/bin/env pwsh
# Fetches the DXC release the shader library's DXIL forms are compiled with.
#
#   pwsh tools/fetch-dxc.ps1            this machine's platform, into third-party/dxc
#   pwsh tools/fetch-dxc.ps1 -Force     re-download over what is there
#
# Pinned, because the DXIL is committed and checked byte for byte, and a different DXC build -- the one the
# Vulkan SDK carries beside slangc, say -- emits different bytes for the same shader. The release is read out
# of ShaderModules.json (dxcVersion), so the compiler fetched and the output committed cannot drift apart.
# It lands in the gitignored third-party/, which is where tools/OpenIGI.Tools.ShaderBuild looks for it
# (OPENIGI_DXC overrides). Only Windows and Linux: Microsoft publishes no macOS build of DXC.

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $root 'src/Rendering/OpenIGI.Shaders/ShaderModules.json') -Raw | ConvertFrom-Json
$version = $manifest.dxcVersion
if (-not $version) {
    throw 'ShaderModules.json pins no dxcVersion.'
}

$destination = Join-Path $root 'third-party/dxc'
$executable = if ($IsWindows) { Join-Path $destination 'bin/x64/dxc.exe' } else { Join-Path $destination 'bin/dxc' }

if ((Test-Path $executable) -and -not $Force) {
    $reported = & $executable --version 2>&1 | Out-String
    if ($reported -match [regex]::Escape("- $version")) {
        Write-Host "dxc $version already in $destination"
        return
    }

    Write-Host "dxc in $destination is not $version; replacing it"
}

if ($IsMacOS) {
    throw 'DXC publishes no macOS build; DXIL is compiled on Windows or Linux.'
}

# The asset names carry the build's date rather than the version, so they are read off the tagged release.
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/microsoft/DirectXShaderCompiler/releases/tags/v$version"
$pattern = if ($IsWindows) { '^dxc_.*\.zip$' } else { '^linux_dxc_.*\.tar\.gz$' }
$asset = $release.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
if (-not $asset) {
    throw "Release v$version has no asset matching $pattern."
}

$archive = Join-Path ([System.IO.Path]::GetTempPath()) $asset.name
Write-Host "fetching  dxc $version <- $($asset.name)"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive

if (Test-Path $destination) {
    Remove-Item -Recurse -Force $destination
}

New-Item -ItemType Directory -Force $destination | Out-Null
if ($IsWindows) {
    Expand-Archive -Path $archive -DestinationPath $destination -Force
}
else {
    tar -xzf $archive -C $destination
}

Remove-Item $archive
$reported = & $executable --version 2>&1 | Out-String
Write-Host $reported.Trim()
