<#
.SYNOPSIS
    Captures the PBR viewpoints twice, with the derived maps off and on, and reports what moved.

.DESCRIPTION
    The gate for every change to the material pipeline. A phase of work that cannot move a pixel on
    the close-up wall has not landed, and without this the only way to know that was to look at two
    screenshots and believe they differed.

    THE -lockstep TRAP. -lockstep is what makes a capture reproducible, and it also ignores
    settings.cfg -- so a run that does not say otherwise renders with the DEFAULTS, where
    r_lighting is 'baked' and shading is texture times lightmap. Under baked lighting the G-buffer's
    normal and roughness are not read by anything, so every change to them measures as zero. Four
    wrong conclusions came out of that in one session. This script therefore always states the whole
    lighting stack explicitly with +gfx_preset, and -Preset is the only thing that may change it.

.PARAMETER Strength
    What r_material_detail is set to for the 'on' capture. The preset's own value is ignored: ultra
    ships zero, which would compare nothing against nothing.

.PARAMETER DebugView
    Capture a G-buffer channel (roughness, metallic, materialao, normal, albedo, material) instead of
    the shaded scene. Use it to answer 'is the channel reaching the shader' before asking whether the
    lighting responded to it.

.EXAMPLE
    pwsh tools/compare-pbr.ps1
    pwsh tools/compare-pbr.ps1 -Strength 2 -DebugView roughness -KeepImages
#>
[CmdletBinding()]
param(
    [double] $Strength = 1.0,
    [string] $Preset = 'ultra',
    [string] $DebugView = '',
    [int] $Frames = 30,
    [string] $Size = '960x540',
    [string] $OutputDirectory = '',
    [string[]] $Viewpoint = @(),
    [string[]] $Extra = @(),
    [switch] $KeepImages
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$game = Join-Path $root 'apps/OpenIGI.Desktop/bin/Release/net10.0/OpenIGI.Desktop.exe'

if (-not (Test-Path $game)) {
    throw "No build at $game. Run: dotnet build apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj -c Release"
}

# The three fixed viewpoints, all in mission 1 and all found with the `campos` console command.
# Ambient-lit open ground is deliberately NOT among them: it is the surface a derived normal map has
# least to say about, and it is what the pipeline had been judged on.
$views = @(
    @{ Name = 'wall';     Mission = 1; Args = @('-campos', '6000.614,-13655.941,42583.133', '-yaw', '-1.5708', '-pitch', '0');
       Note = 'a sunlit concrete wall at arm''s length -- direct light across a derived normal' }
    @{ Name = 'metal';    Mission = 1; Args = @('-campos', '5970.614,-13675.941,42583.133', '-yaw', '-1.5708', '-pitch', '0');
       Note = 'the fuel tanks, the dark plant and the wire fence -- what metallic and gloss should act on' }
    @{ Name = 'interior'; Mission = 1; Args = @('-campos', '5994.614,-13655.941,42583.133', '-yaw', '-1.5708', '-pitch', '0');
       Note = 'the office, lit by its own ceiling tubes over a parquet floor -- lamp specular and ambient' }
)

if ($Viewpoint.Count -gt 0) {
    $views = $views | Where-Object { $Viewpoint -contains $_.Name }
    if (-not $views) { throw "No viewpoint matched: $($Viewpoint -join ', ')" }
}

$output = if ($OutputDirectory) { $OutputDirectory } else { Join-Path ([System.IO.Path]::GetTempPath()) "openigi-pbr" }
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Invoke-Capture {
    param([hashtable] $View, [string] $Tag, [double] $Detail)

    $bmp = Join-Path $output "$($View.Name)_$Tag.bmp"

    # Order matters: gfx_preset rewrites every graphics variable it owns, so the two this comparison
    # is actually about have to be set AFTER it or the preset's own values win.
    $console = @('+gfx_preset', $Preset, '+r_material_detail', ([string] $Detail), '+r_pbr', $(if ($Detail -gt 0) { '1' } else { '0' }))
    if ($DebugView) { $console += @('+r_debug_view', $DebugView) }
    $console += $Extra

    $arguments = @(
        '-mission', ([string] $View.Mission), '-lockstep', '-skipcutscene', '-freecam',
        '-size', $Size, '-frames', ([string] $Frames), '-screenshot', $bmp
    ) + $View.Args + $console

    & $game @arguments 2>&1 | Out-File -FilePath (Join-Path $output "$($View.Name)_$Tag.log") -Encoding utf8
    if (-not (Test-Path $bmp)) { throw "No capture written for $($View.Name)/$Tag; see $output" }
    return $bmp
}

$label = if ($DebugView) { "G-buffer '$DebugView'" } else { 'shaded scene' }
Write-Host "compare-pbr: $label, preset $Preset, r_material_detail 0 -> $Strength" -ForegroundColor Cyan
Write-Host ''

$failures = 0

foreach ($view in $views) {
    $off = Invoke-Capture -View $view -Tag 'off' -Detail 0.0
    $on  = Invoke-Capture -View $view -Tag 'on'  -Detail $Strength

    & uv run --quiet --with pillow --with numpy python (Join-Path $PSScriptRoot 'pbr/diff_captures.py') $off $on --label $view.Name
    if ($LASTEXITCODE -ne 0) { $failures++ }

    if ($KeepImages) {
        & uv run --quiet --with pillow python -c @"
import sys
from PIL import Image
for path in sys.argv[1:]:
    with Image.open(path) as image:
        image.save(path[:-4] + '.png')
"@ $off $on
    }
}

Write-Host ''
Write-Host "Captures in $output" -ForegroundColor DarkGray
Write-Host 'A phase that reports changed 0.00% on the wall viewpoint has not landed.' -ForegroundColor DarkGray
exit $failures
