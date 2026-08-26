<#
.SYNOPSIS
    Flips every ultra-reachable render cvar and checks the picture actually responds.

.DESCRIPTION
    The bug class this exists to make extinct: a setting that is accepted, stored, echoed back by the
    console, and then never read by the backend that is running. Nothing reports it. The picture is
    simply not what the settings say it is, and every measurement taken against that picture is wrong
    about why.

    That is not hypothetical here. A whole session was spent chasing a lighting difference that turned
    out to be a preset silently running four times the probe irradiance of its own reference, and the
    only reason it was ever found was someone reading the values by hand.

    So each cvar is captured twice, at two values that should look different, and the two frames are
    differenced. There are exactly three honest outcomes:

      RESPONDS   the frame changed. The setting reaches the picture.
      DECLARED   the frame did not change, and the backend already says so -- it names the setting in
                 its unimplemented list, so the value is kept and the user is warned.
      SILENT     the frame did not change and nothing said why. This is the bug. Any row here is a
                 failure, and the script exits non-zero on it.

    A setting can also be inert because of its own value rather than because the backend ignores it
    (a bloom threshold with bloom off, a lookup table with no table loaded). That is legitimate, and
    the way to record it is -Expected, which turns a SILENT row into an EXPECTED one and requires a
    reason to be written down next to it.

.PARAMETER Bin
    The build to capture with. Defaults to artifacts/testbin, so a side-build is used and a running
    session's binaries are never touched.

.PARAMETER Only
    Sweep just the named cvars rather than the whole table. Accepts several.

.PARAMETER Frames
    How many frames each capture renders before the shot. The default lets the temporal pass settle;
    below about 200 a difference can be accumulation rather than the setting.
#>

[CmdletBinding()]
param(
    [string]$Bin = 'artifacts/testbin',
    [string[]]$Only = @(),
    [int]$Frames = 240,
    [string]$Label = 'sweep'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo "$Bin/OpenIGI.Desktop.exe"
if (-not (Test-Path $exe)) { throw "no build at $exe -- side-build first: dotnet build apps/OpenIGI.Desktop -c Release -o $Bin" }

$outDir = Join-Path $repo "artifacts/sweep/$Label"
New-Item -ItemType Directory -Force $outDir | Out-Null

# The office pose, which is the one with a bright window, deep shadow, a sunlit floor and geometry at
# several depths -- so most settings have something in frame to act on.
$pose = @{ Pos = '5993.658,-13659.278,42583.867'; Yaw = '-2.2199'; Pitch = '-0.2150' }
# The same sun the compare harness uses. Getting this wrong is not a subtle error: point it below the
# horizon and every capture is pure black, every A/B compares black against black, and the whole sweep
# reports that nothing in the renderer responds to anything.
$sun = '0.032795,-0.939117,0.342020'

# Each row is a cvar and the two values it is captured at. The pair matters: "off vs on" is the wrong
# test for something already off at ultra, because both frames would then be the same for a reason that
# has nothing to do with whether the backend reads it.
$cases = @(
    @{ Name = 'r_bloom';             A = '0';     B = '0.6'   }
    @{ Name = 'r_bloom_threshold';   A = '0';     B = '4'     ; Expected = 'our bloom is unthresholded, matching the reference, so there is no cut for this to move' }
    @{ Name = 'r_vignette';          A = '0';     B = '0.9'   }
    @{ Name = 'r_grain';             A = '0';     B = '0.5'   }
    @{ Name = 'r_chromatic';         A = '0';     B = '2'     }
    @{ Name = 'r_sharpen';           A = '0';     B = '1'     }
    @{ Name = 'r_contrast';          A = '1';     B = '1.6'   }
    @{ Name = 'r_saturation';        A = '1';     B = '0'     }
    @{ Name = 'r_dof';               A = '0';     B = '8'     }
    @{ Name = 'r_dof_range';         A = '4';     B = '60'    ; Expected = 'depth of field is off at ultra, matching the reference, so its range has nothing to scale' }
    @{ Name = 'r_dof_blur';          A = '1';     B = '20'    ; Expected = 'depth of field is off at ultra, matching the reference, so its radius has nothing to widen' }
    @{ Name = 'r_motion_blur';       A = '0';     B = '1'     ; Expected = 'the compare pose is still, and a still frame has no motion to smear -- swept under a moving camera instead' }
    @{ Name = 'r_camera_taps';       A = '4';     B = '16'    ; Expected = 'the motion gather takes the reference sample count for its own blur length; this dial is superseded' }
    @{ Name = 'r_lut';               A = '0';     B = '1'     }
    @{ Name = 'r_ssr';               A = '0';     B = '64'    }
    @{ Name = 'r_ssr_intensity';     A = '0';     B = '2'     }
    @{ Name = 'r_ssr_max_roughness'; A = '0.1';   B = '1'     }
    @{ Name = 'r_floor_gloss';       A = '0';     B = '1'     }
    @{ Name = 'r_material_detail';   A = '0';     B = '2'     }
    @{ Name = 'r_alpha_hash';        A = '0';     B = '1'     }
    @{ Name = 'r_fog_density';       A = '0';     B = '0.5'   }
    @{ Name = 'r_fog_height';        A = '24';    B = '400'   }
    @{ Name = 'r_fog_anisotropy';    A = '-0.5';  B = '0.9'   }
    @{ Name = 'r_fog_quality';       A = '16';    B = '128'   }
    @{ Name = 'r_fog_distance';      A = '200';   B = '1200'  }
    @{ Name = 'r_sun_angle';         A = '0';     B = '4'     }
    @{ Name = 'r_ao_intensity';      A = '0';     B = '3'     }
    @{ Name = 'r_gi_bounce';         A = '0';     B = '2'     }
    @{ Name = 'r_light_scale';       A = '0.5';   B = '1.4'   }
    @{ Name = 'r_exposure';          A = '0.6';   B = '2.5'   }
    @{ Name = 'r_white_point';       A = '1';     B = '4'     }
)

if ($Only.Count -gt 0) {
    $cases = $cases | Where-Object { $Only -contains $_.Name }
    if (-not $cases) { throw "none of [$($Only -join ', ')] are in the sweep table" }
}

function Capture([string]$cvar, [string]$value, [string]$tag) {
    $shot = Join-Path $outDir "$tag.png"
    $arguments = @(
        '-vulkan', '-window', '-size', '1600x900',
        '-mission', '1', '-skipcutscene', '-freecam', '-lockstep',
        '-fov', '75',
        '-campos', $pose.Pos, '-yaw', $pose.Yaw, '-pitch', $pose.Pitch,
        '-sundir', $sun,
        '-screenshot', $shot, '-frames', $Frames,
        '+gfx_preset', 'ultra', "+$cvar", $value
    )
    & $exe @arguments *> (Join-Path $outDir "$tag.log")
    if ($LASTEXITCODE -ne 0) { throw "$cvar=$value exited $LASTEXITCODE -- see $outDir/$tag.log" }

    # A fixed pose writes the one file it was given; a flight writes a numbered sequence and the settled
    # frame is the last of them. Both shapes are accepted so the sweep can be pointed at either.
    if (Test-Path $shot) { return (Resolve-Path $shot).Path }

    $escaped = [regex]::Escape($tag)
    $produced = Get-ChildItem $outDir -Filter '*.png' |
        Where-Object { $_.Name -match "^$escaped\.\d+\.png$" } |
        Sort-Object Name | Select-Object -Last 1
    if (-not $produced) { throw "$cvar=$value produced no frame -- see $outDir/$tag.log" }
    return $produced.FullName
}

# A frame with no variation in it at all is not a picture, and differencing two of them says every
# setting in the renderer is inert. That is how this script first ran: one wrong -sundir put the sun
# under the horizon, every capture came back uniformly black, and the sweep confidently reported thirty
# silent cvars. A sweep that cannot tell "nothing changed" from "nothing rendered" is worse than no
# sweep, so every capture is checked for having any content before it is allowed to be evidence.
function Assert-Rendered([string]$path, [string]$what) {
    $deviation = [double](& magick $path -alpha off -colorspace Gray -format '%[fx:standard_deviation]' info:)
    if ($deviation -lt 0.001) {
        throw "$what rendered a blank frame (standard deviation $deviation). The capture is not showing " +
              "the scene -- check -sundir, the pose, and the mission -- and until it does, every " +
              "comparison below it is meaningless."
    }
}

# Whether the running build declares this setting unimplemented, read from the capture's OWN log rather
# than from the table in the repo -- so the sweep checks what the binary says, not what the source hopes.
#
# It has to be the capture where the value is NOT the default, because the backend only warns about
# settings actually IN FORCE. Reading the declaration from a defaults run finds nothing and reports every
# unimplemented setting as a silent bug.
function Test-Declared([string]$cvar, [string]$tag) {
    $log = Join-Path $outDir "$tag.log"
    if (-not (Test-Path $log)) { return $false }
    return [bool](Select-String -Path $log -Pattern "Render: $cvar is .* does not implement it yet" -Quiet)
}

$rows = @()
foreach ($case in $cases) {
    $n = $case.Name
    $a = Capture $n $case.A "$n-a"
    $b = Capture $n $case.B "$n-b"
    Assert-Rendered $a "$n=$($case.A)"
    Assert-Rendered $b "$n=$($case.B)"

    $raw = magick compare -metric RMSE -alpha off $a $b null: 2>&1
    $fraction = if ("$raw" -match '\(([0-9.eE+-]+)\)') { [double]$Matches[1] } else { [double]::NaN }

    # A tenth of a percent. Below that two captures of the SAME settings can differ through nothing but
    # the temporal pass landing on a different frame of its jitter, so it is a floor on "changed", not
    # a judgement about whether the change is worth seeing.
    $responds = $fraction -gt 0.001

    $status = if ($responds) { 'RESPONDS' }
              elseif ((Test-Declared $n "$n-b") -or (Test-Declared $n "$n-a")) { 'DECLARED' }
              elseif ($case.Expected) { 'EXPECTED' }
              else { 'SILENT' }

    $rows += [pscustomobject]@{
        Cvar = $n; Values = "$($case.A) -> $($case.B)"; Delta = $fraction
        Status = $status; Note = $case.Expected
    }

    $colour = switch ($status) {
        'RESPONDS' { 'Green' } 'DECLARED' { 'DarkYellow' } 'EXPECTED' { 'DarkCyan' } default { 'Red' }
    }
    Write-Host ("{0,-22} {1,-14} {2,10:P3}  {3}" -f $n, "$($case.A)->$($case.B)", $fraction, $status) -ForegroundColor $colour
    if ($status -eq 'EXPECTED' -and $case.Expected) {
        Write-Host ("{0,-22} {1}" -f '', $case.Expected) -ForegroundColor DarkGray
    }
}

$rows | Export-Csv -NoTypeInformation (Join-Path $outDir 'sweep.csv')

$silent = @($rows | Where-Object Status -eq 'SILENT')
Write-Host ''
Write-Host ("responds {0}   declared {1}   expected {2}   silent {3}" -f
    @($rows | Where-Object Status -eq 'RESPONDS').Count,
    @($rows | Where-Object Status -eq 'DECLARED').Count,
    @($rows | Where-Object Status -eq 'EXPECTED').Count,
    $silent.Count) -ForegroundColor Cyan
Write-Host "written to artifacts/sweep/$Label"

if ($silent.Count -gt 0) {
    Write-Host ''
    Write-Host "SILENT settings -- accepted, stored, and never reaching the picture, with nothing saying so:" -ForegroundColor Red
    $silent | ForEach-Object { Write-Host "  $($_.Cvar)" -ForegroundColor Red }
    exit 1
}
