# A fixed reference for judging lamp lighting and lamp shadows.
#
# Every earlier attempt at this was measured by eye or by whole-image mean brightness with the eye
# adaptation running, which moves with the picture and cancels exactly what is being measured. This
# pins the three things that have to be still:
#
#   the exposure   -r_autoexposure 0, so a brightness in the image is a brightness in the scene
#   the camera     one spot in mission 1's office, looking at the floor the fixtures light
#   the lights     ONE lamp, not the room's four -- three evenly spaced battens light every surface
#                  from every side, and nothing in the room casts a shadow anybody could see
#
# The last is the important one. A shadow test under even lighting measures nothing.
#
# usage: pwsh tools/lamp-probe.ps1 [-Out D:\probe] [-Lamp 50] [-Size 1280x720]

param(
    [string]$Out = 'D:\lamp-probe',
    [int]$Lamp = 50,
    [string]$Size = '1280x720',
    [int]$Frames = 90,
    [string]$Preset = 'ultra'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'apps\OpenIGI.Desktop\bin\Release\net10.0\OpenIGI.Desktop.exe'
$fixtures = Join-Path $root 'apps\OpenIGI.Desktop\bin\Release\net10.0\Assets\fixtures\mission-1.json'

if (-not (Test-Path $exe)) { throw "Build Release first: $exe" }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# Directly beneath the chosen lamp, looking straight down at the floor.
#
# The view matters as much as the exposure. Looking ACROSS a room picks up floor that the desk and the
# cabinets legitimately shadow, and a shadow test cannot tell a bug from a correct shadow if it cannot
# say which patch of floor is supposed to be lit. Standing under the lamp and looking down, the centre
# of the frame is the one place in the room with a guaranteed clear line to the source: whatever else
# is wrong, that patch has to be lit.
$camera = @('-campos', '5992.36,-13657.91,42583.4', '-yaw', '-2.3545', '-pitch', '-0.1361')

# Keep the full set aside, then leave exactly one fixture in place. The emitters and sub-meshes stay,
# so the fittings still glow -- it is the LIGHTS being reduced to one, not the lamps being removed.
#
# The backup lives beside the fixtures rather than in the output directory, because the run REPLACES
# the live file: a second run pointed at a different output would otherwise take the one-lamp file it
# just wrote as its full set, and there would be nothing left to select from.
$full = "$fixtures.full"
if (-not (Test-Path $full)) { Copy-Item $fixtures $full }

$doc = Get-Content $full -Raw | ConvertFrom-Json
if ($Lamp -ge $doc.Fixtures.Count) { throw "No fixture $Lamp; the file holds $($doc.Fixtures.Count)." }
$doc.Fixtures = @($doc.Fixtures[$Lamp])
$doc | ConvertTo-Json -Depth 8 | Set-Content $fixtures -Encoding UTF8

Write-Host "one lamp: fixture $Lamp at ($($doc.Fixtures[0].PositionX), $($doc.Fixtures[0].PositionY), $($doc.Fixtures[0].PositionZ))"

# Pinned exposure. Without this a darker scene simply opens the eye and the measurement says nothing.
$common = @(
    '-mission', '1', '-lockstep', '-skipcutscene', '-freecam',
    '-size', $Size, '-frames', $Frames,
    '+gfx_preset', $Preset,
    '+r_time_of_day', '22',
    '+r_autoexposure', '0',
    '+r_exposure', '5'
) + $camera

foreach ($case in @(
    @{ Name = 'off'; Args = @('+r_local_light_shadows', '0') },
    @{ Name = 'on';  Args = @('+r_local_light_shadows', '1') }
)) {
    $bmp = Join-Path $Out "lamp-$($case.Name).bmp"
    & $exe @common @($case.Args) '-screenshot' $bmp | Out-Null
    if (-not (Test-Path $bmp)) { throw "no capture for $($case.Name)" }
    Write-Host "wrote $bmp"
}

Write-Host ''
Write-Host 'The question these two answer: with one lamp overhead, does "on" differ from "off" by a'
Write-Host 'SHADOW -- shaped like the desk and the chair -- or by a uniform darkening? A uniform'
Write-Host 'darkening is the trace failing, however plausible the picture looks on its own.'
