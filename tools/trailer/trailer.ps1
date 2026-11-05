<#
.SYNOPSIS
    Captures, composites and edits the OpenIGI introduction video.

.DESCRIPTION
    One script, five steps, all of them ffmpeg over the shaders in tools/trailer/shaders (run on
    the GPU through libplacebo):

      capture   Render one scripted flight once per graphics preset, under -lockstep, so every
                take of the shot is the same frames of the same moment. Encodes each take to
                shots/<shot>/<preset>.mkv and deletes the BMP frames.
      compare   Put two takes of a shot through a comparison shader: wipe, lens or resolve.
      fx        Run a clip through crt, glitch or cinematic (several in order), keeping its audio.
      caption   Lay an animated lower-third callout over a clip.
      title     Make a title card, over black or over a blurred clip.
      assemble  Cut an edit list into the final 1440p60 (or 2160p60) YouTube master with music.

    Everything lands under -Work (default artifacts/trailer, which git ignores). Run captures one
    at a time; each opens a game window.

.EXAMPLE
    ./tools/trailer/trailer.ps1 capture -Shot m1-base -Mission 1 -CamPos 812.5,-30.2,40 -Yaw 1.2 -Pitch -0.1 `
        -CamEnd 860,-10,52 -YawEnd 1.6 -Seconds 8
    ./tools/trailer/trailer.ps1 compare -Shot m1-base -Effect wipe -Reveal
    ./tools/trailer/trailer.ps1 fx -In clips/gameplay-gi.mp4 -Effect cinematic
    ./tools/trailer/trailer.ps1 assemble -Edl tools/trailer/edl.example.txt -Music music.mp3
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('capture', 'compare', 'fx', 'caption', 'title', 'assemble')]
    [string] $Step,

    [string] $Work = (Join-Path ([Environment]::GetFolderPath('MyVideos')) 'OpenIGI-Trailer'),

    # capture
    [string] $Shot,
    [string] $Title = 'igi1',
    [int] $Mission = 1,
    [double[]] $CamPos,
    [double] $Yaw = 0,
    [double] $Pitch = 0,
    [double[]] $CamEnd,
    [Nullable[double]] $YawEnd,
    [Nullable[double]] $PitchEnd,
    [Nullable[double]] $Fov,
    [double] $Seconds = 8,
    [double] $Shake = 0.12,
    [int] $Warmup = 180,
    [string] $Size = 'screen',
    [int] $CaptureRate = 60,
    [string] $FramesDir = (Join-Path ([IO.Path]::GetTempPath()) 'openigi-trailer-frames'),
    [string[]] $Presets = @('vanilla', 'ultra'),
    [string] $Tag,
    [string[]] $Extra = @(),
    [string] $Exe,
    [string] $Game,
    [switch] $KeepFrames,

    # compare
    [ValidateSet('split', 'wipe', 'lens', 'resolve')]
    [string] $Effect,
    [string] $Left = 'vanilla',
    [string] $Right = 'ultra',
    [string] $LeftLabel = 'ORIGINAL  ·  2000',
    [string] $RightLabel = 'OPENIGI  ·  ULTRA',
    [switch] $NoLabels,
    [switch] $Reveal,

    # fx / caption / title
    [string] $In,
    [string[]] $Effects,
    [double[]] $At = @(),
    [hashtable] $Set = @{},
    [string] $Text,
    [string] $Sub,
    [string] $Note,
    [double] $For = 3.5,
    [string] $Background,

    # assemble
    [string] $Edl,
    [string] $Music,
    [double] $MusicVolume = 0.9,
    [double] $GameVolume = 0.45,
    [switch] $Uhd,
    [switch] $Cpu,

    [string] $Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Fps = 60
# The master's frame: 1440p60, the recordings' own; -Uhd scales the finished master to 2160p for YouTube's higher bitrate.
$Width = 2560
$Height = 1440
$Inv = [cultureinfo]::InvariantCulture
$ShaderDir = Join-Path $PSScriptRoot 'shaders'
$Work = [IO.Path]::GetFullPath($Work)
$Tmp = Join-Path $Work 'tmp'
$Clips = Join-Path $Work 'clips'
New-Item -ItemType Directory -Force $Work, $Tmp, $Clips | Out-Null

# The colour every intermediate and the master are tagged with, so YouTube does not guess.
$Bt709 = @('-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv')
# Encodes run on the GPU (NVENC) unless -Cpu asks for x264: the intermediates near-lossless 4:4:4, the segments 4:2:0.
if ($Cpu)
{
    $Mezzanine = @('-c:v', 'libx264', '-preset', 'slow', '-crf', '10', '-pix_fmt', 'yuv444p') + $Bt709
    $SegmentCodec = @('-c:v', 'libx264', '-preset', 'medium', '-crf', '12')
    $Decode = @()
}
else
{
    $Mezzanine = @('-c:v', 'h264_nvenc', '-profile:v', 'high444p', '-preset', 'p5', '-rc', 'vbr', '-cq', '12', '-b:v', '0', '-pix_fmt', 'yuv444p') + $Bt709
    $SegmentCodec = @('-c:v', 'h264_nvenc', '-preset', 'p5', '-rc', 'vbr', '-cq', '14', '-b:v', '0')
    # Decoding stays on the CPU: nineteen CUDA decoders at once handed xfade frames before they were filled, which
    # showed as solid green bands in the master.
    $Decode = @()
}

function Invoke-Ffmpeg([string[]] $Arguments)
{
    # Filtergraphs name shaders, fonts and text files relative to tmp, so the paths need no escaping.
    Push-Location $Tmp
    try
    {
        # Every command is kept, quoted, so a bad output can be traced to exactly what made it.
        $quoted = $Arguments | ForEach-Object { if ("$_" -match '[\s;|]') { '"{0}"' -f $_ } else { "$_" } }
        Add-Content -Encoding utf8NoBOM (Join-Path $Tmp 'commands.log') ("ffmpeg " + ($quoted -join ' '))
        # Warnings are read as well as shown: a shader that fails to compile is only a warning to libplacebo, which then
        # passes the frames through untouched, and the clip looks plausible while being wrong.
        $warnings = & ffmpeg -hide_banner -loglevel warning -nostats -y @Arguments 2>&1 | ForEach-Object { "$_" }
        $warnings | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed ($LASTEXITCODE): ffmpeg $($Arguments -join ' ')" }
        if ($warnings -match 'Failed executing hook|shaderc compile status') { throw "A shader failed: ffmpeg $($Arguments -join ' ')" }
    }
    finally { Pop-Location }
}

function Get-Duration([string] $Path)
{
    [double]::Parse((& ffprobe -v error -show_entries format=duration -of csv=p=0 $Path).Trim(), $Inv)
}

function Test-Audio([string] $Path)
{
    [bool](& ffprobe -v error -select_streams a -show_entries stream=index -of csv=p=0 $Path)
}

function Format-Num([double] $Value) { $Value.ToString('0.0#####', $Inv) }

function Resolve-Input([string] $Path)
{
    foreach ($candidate in @($Path, (Join-Path $Work $Path)))
    {
        if (Test-Path $candidate) { return (Resolve-Path $candidate).Path }
    }
    throw "No such file: $Path"
}

function Get-OutPath([string] $Default)
{
    $path = if ($Out) { $Out } else { Join-Path $Clips $Default }
    [IO.Path]::GetFullPath($path, (Get-Location).Path)
}

# Copies a shader into tmp with its #defines rewritten and returns the copy's file name.
function New-Shader([string] $Name, [hashtable] $Defines)
{
    $source = Get-Content -Raw (Join-Path $ShaderDir "$Name.glsl")
    foreach ($key in $Defines.Keys)
    {
        $pattern = "(?m)^#define\s+$([regex]::Escape($key))\s+[^/\r\n]*"
        if ($source -notmatch $pattern) { throw "$Name.glsl has no #define $key" }
        $value = $Defines[$key]
        if ($value -is [double] -or $value -is [int] -or $value -is [single]) { $value = Format-Num $value }
        $source = [regex]::Replace($source, $pattern, "#define $key $value ")
    }
    $file = '{0}-{1}.glsl' -f $Name, [guid]::NewGuid().ToString('N').Substring(0, 8)
    Set-Content -NoNewline -Encoding utf8NoBOM (Join-Path $Tmp $file) $source
    $file
}

function Get-Placebo([string] $Shader, [string] $Size = '')
{
    $dims = if ($Size) { "$Size`:" } else { '' }
    "libplacebo=$($dims)custom_shader_path=$Shader`:format=yuv444p:colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv"
}

function Initialize-Font
{
    $font = Join-Path $Tmp 'font.ttf'
    if (-not (Test-Path $font)) { Copy-Item "$env:WINDIR/Fonts/bahnschrift.ttf" $font }
}

function New-TextFile([string] $Content)
{
    $file = 'text-{0}.txt' -f [guid]::NewGuid().ToString('N').Substring(0, 8)
    Set-Content -NoNewline -Encoding utf8NoBOM (Join-Path $Tmp $file) $Content
    $file
}

# Scales anything to the master's frame and rate, letterboxing rather than cropping.
$Conform = "scale=$($Width):$($Height):force_original_aspect_ratio=decrease:flags=lanczos:out_color_matrix=bt709:out_range=tv," +
    "pad=$($Width):$($Height):(ow-iw)/2:(oh-ih)/2:black,setsar=1,fps=$Fps,setpts=N/($Fps*TB)"

switch ($Step)
{
    'capture'
    {
        if (-not $Shot -or -not $CamPos) { throw 'capture needs -Shot and -CamPos (read them in game with the campos console command).' }
        if (-not $Exe)
        {
            $Exe = Get-ChildItem (Join-Path $PSScriptRoot '../../apps/OpenIGI.Desktop/bin/Release') -Recurse -Filter OpenIGI.Desktop.exe |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
            if (-not $Exe) { throw 'No Release build found; run dotnet build -c Release OpenIGI.slnx first.' }
        }
        if (Get-Process OpenIGI.Desktop -ErrorAction SilentlyContinue) { throw 'OpenIGI is already running; captures run one at a time.' }

        # At -CaptureRate frames a second: lockstep blends between the 30 Hz ticks, so 60 is smooth motion, not repeats.
        $frames = [int][math]::Round($Seconds * $CaptureRate)
        $shotDir = Join-Path $Work "shots/$Shot"
        New-Item -ItemType Directory -Force $shotDir | Out-Null
        # 'screen' is a borderless window over the whole monitor, the only way to get its full height: a decorated window
        # loses its title bar's worth, 2560x1421 on a 1440p display.
        $window = if ($Size -eq 'screen') { @('-borderless') } else { @('-window', '-size', $Size) }
        $common = @('-title', $Title, '-mission', $Mission, '-lockstep', '-capturerate', $CaptureRate, '-skipcutscene') + $window + @(
            '-campos', (($CamPos | ForEach-Object { Format-Num $_ }) -join ','),
            '-yaw', (Format-Num $Yaw), '-pitch', (Format-Num $Pitch),
            '-flightdelay', $Warmup, '-flightframes', $frames, '-frames', ($Warmup + $frames))
        if ($Game) { $common += @('-game', $Game) }
        if ($CamEnd) { $common += @('-camend', (($CamEnd | ForEach-Object { Format-Num $_ }) -join ',')) }
        if ($null -ne $YawEnd) { $common += @('-yawend', (Format-Num $YawEnd)) }
        if ($null -ne $PitchEnd) { $common += @('-pitchend', (Format-Num $PitchEnd)) }
        if ($null -ne $Fov) { $common += @('-fov', (Format-Num $Fov)) }
        # A shake of zero with no end pose would be no flight at all, and a flight is what writes frames.
        $common += @('-camshake', (Format-Num ([math]::Max($Shake, 0.001))))

        foreach ($preset in $Presets)
        {
            $label = if ($Tag) { "$preset-$Tag" } else { $preset }
            # Raw frames are tens of megabytes each at 2160p; they go to -FramesDir, a drive with room, and are encoded away.
            $frameDir = Join-Path $FramesDir "$Shot-$label"
            Remove-Item -Recurse -Force $frameDir -ErrorAction SilentlyContinue
            New-Item -ItemType Directory -Force $frameDir | Out-Null

            Write-Host "[$Shot] $label : $frames frames after $Warmup warm-up"
            $arguments = $common + @('-screenshot', (Join-Path $frameDir 'f.bmp'), '+gfx_preset', $preset) + $Extra
            # The game is a WinExe, so & would not wait for it.
            $quoted = $arguments | ForEach-Object { if ("$_" -match '\s') { '"{0}"' -f $_ } else { "$_" } }
            Start-Process -FilePath $Exe -ArgumentList $quoted -WorkingDirectory (Split-Path $Exe) -Wait -WindowStyle Normal
            $written = @(Get-ChildItem $frameDir -Filter 'f.*.bmp').Count
            if ($written -ne $frames) { Write-Warning "[$Shot] $label wrote $written frames, expected $frames" }
            if ($written -eq 0) { throw "[$Shot] $label wrote no frames; check the log beside the exe." }

            Invoke-Ffmpeg (@('-framerate', $CaptureRate, '-start_number', $Warmup, '-i', (Join-Path $frameDir 'f.%04d.bmp'),
                # A borderless window is a pixel larger than the screen each way; the crop takes it back to 2560x1440.
                '-vf', 'crop=iw-mod(iw\,16):ih-mod(ih\,16):0:0,scale=out_color_matrix=bt709:out_range=tv') + $Mezzanine + @((Join-Path $shotDir "$label.mkv")))
            if (-not $KeepFrames) { Remove-Item -Recurse -Force $frameDir }
        }
        Write-Host "Takes in $shotDir"
    }

    'compare'
    {
        if (-not $Shot -or -not $Effect) { throw 'compare needs -Shot and -Effect.' }
        $shotDir = Join-Path $Work "shots/$Shot"
        $a = Join-Path $shotDir "$Left.mkv"
        $b = Join-Path $shotDir "$Right.mkv"
        $duration = [math]::Min((Get-Duration $a), (Get-Duration $b))

        $defines = @{ FPS = $Fps }
        $name = switch ($Effect) { 'split' { 'compare_split' } 'wipe' { 'compare_wipe' } 'lens' { 'compare_lens' } 'resolve' { 'era_resolve' } }
        if ($Reveal -and $Effect -in 'wipe', 'lens') { $defines.FINISH = [math]::Max(0.0, $duration - 1.6) }

        # The split's beats: in from the right, hold at the centre, give the remaster more, hold, and (with -Reveal) leave
        # it the frame. The labels below ride the line from the same keys.
        $keys = @()
        if ($Effect -eq 'split')
        {
            $end = if ($Reveal) { $duration - 0.2 } else { $duration + 10 }
            $mid = [math]::Max(2.6, $duration * 0.45)
            $keys = @(@(0.0, 1.0), @(0.4, 1.0), @(1.0, 0.5), @($mid, 0.5), @(($mid + 0.6), 0.28), @(($end - 0.9), 0.28), @($end, 0.0))
            $defines['KEY_COUNT'] = "$($keys.Count)"
            $defines['KT'] = 'float[KEY_COUNT](' + (($keys | ForEach-Object { Format-Num $_[0] }) -join ', ') + ')'
            $defines['KX'] = 'float[KEY_COUNT](' + (($keys | ForEach-Object { Format-Num $_[1] }) -join ', ') + ')'
        }
        foreach ($key in $Set.Keys) { $defines[$key] = $Set[$key] }
        $shader = New-Shader $name $defines

        $graph = "[0]$Conform,format=yuv444p[a];[1]$Conform,format=yuv444p[b];[a][b]hstack," +
            (Get-Placebo $shader "w=$($Width):h=$($Height)")

        if (-not $NoLabels -and $Effect -eq 'split')
        {
            # The line's position as the shader computes it, a smootherstep between each pair of keys.
            Initialize-Font
            $p = Format-Num $keys[0][1]
            for ($k = 0; $k -lt $keys.Count - 1; $k++)
            {
                $dx = $keys[$k + 1][1] - $keys[$k][1]
                if ($dx -eq 0) { continue }
                $u = "clip((t-$(Format-Num $keys[$k][0]))/$(Format-Num ($keys[$k + 1][0] - $keys[$k][0])),0,1)"
                $p += "+($(Format-Num $dx))*pow($u,3)*($u*($u*6-15)+10)"
            }
            $gap = 'h/36'
            $style = "fontfile=font.ttf:fontsize=h/34:fontcolor=white:box=1:boxcolor=black@0.45:boxborderw=12:y=h-h/9-th"
            $leftX = "'w*($p)-tw-$gap'"
            $leftA = "'clip((w*($p)-tw-2*$gap)/($gap),0,1)'"
            $rightX = "'w*($p)+$gap'"
            $rightA = "'clip((w-w*($p)-tw-2*$gap)/($gap),0,1)'"
            $graph += ",drawtext=textfile=$(New-TextFile $LeftLabel):x=$($leftX):alpha=$($leftA):$style"
            $graph += ",drawtext=textfile=$(New-TextFile $RightLabel):x=$($rightX):alpha=$($rightA):$style"
        }
        # Labels appear once the split has settled, and leave if the divider runs off.
        elseif (-not $NoLabels -and $Effect -ne 'lens')
        {
            Initialize-Font
            $settle = if ($Effect -eq 'wipe') { 2.4 } else { 0.4 }
            $leave = if ($defines.ContainsKey('FINISH')) { $defines.FINISH } else { 1e6 }
            if ($Effect -eq 'resolve') { $leave = 1.2 }
            $alpha = "'min(clip((t-$(Format-Num $settle))/0.5,0,1),clip(($(Format-Num $leave)-t)/0.4,0,1))'"
            $style = "fontfile=font.ttf:fontsize=h/30:fontcolor=white:shadowcolor=black@0.7:shadowx=2:shadowy=2:alpha=$alpha"
            $graph += ",drawtext=textfile=$(New-TextFile $LeftLabel):x=h/20:y=h-h/12-th:$style"
            if ($Effect -eq 'wipe') { $graph += ",drawtext=textfile=$(New-TextFile $RightLabel):x=w-tw-h/20:y=h-h/12-th:$style" }
        }

        $target = Get-OutPath "$Shot-$Effect.mkv"
        Invoke-Ffmpeg (@('-i', $a, '-i', $b, '-filter_complex', $graph, '-t', (Format-Num $duration), '-an') + $Mezzanine + @($target))
        Write-Host $target
    }

    'fx'
    {
        if (-not $In -or -not $Effects) { throw 'fx needs -In and -Effects (crt, glitch, cinematic; in order).' }
        $source = Resolve-Input $In
        $chain = @("$Conform,format=yuv444p")
        foreach ($fx in $Effects)
        {
            # glitch runs once per -At second; the others once.
            # NaN stands for no time: an if that yields @($null) unrolls to nothing and the loop never runs.
            $times = if ($fx -eq 'glitch' -and $At.Count -gt 0) { $At } else { [double]::NaN }
            foreach ($time in $times)
            {
                $defines = @{ FPS = $Fps }
                if (-not [double]::IsNaN($time)) { $defines.AT = $time }
                $known = Get-Content -Raw (Join-Path $ShaderDir "$fx.glsl")
                foreach ($key in $Set.Keys) { if ($known -match "(?m)^#define\s+$key\s") { $defines[$key] = $Set[$key] } }
                $chain += Get-Placebo (New-Shader $fx $defines)
            }
        }
        $target = Get-OutPath ("{0}-{1}.mkv" -f [IO.Path]::GetFileNameWithoutExtension($source), ($Effects -join '-'))
        Invoke-Ffmpeg (@('-i', $source, '-vf', ($chain -join ','), '-map', '0:v', '-map', '0:a?', '-c:a', 'flac') + $Mezzanine + @($target))
        Write-Host $target
    }

    'caption'
    {
        if (-not $In -or -not $Text) { throw 'caption needs -In and -Text (and optionally -Sub, -At, -For).' }
        Initialize-Font
        $source = Resolve-Input $In
        $start = if ($At.Count -gt 0) { $At[0] } else { 0.6 }
        $s = Format-Num $start
        $e = Format-Num ($start + $For)
        # Slide in from the left with an amber rule beside the text; fade out at the end.
        $slide = "clip((t-$s)/0.45,0,1)"
        $alpha = "'min($slide,clip(($e-t)/0.4,0,1))'"
        $x = "'h/16-h/24*pow(1-$slide,3)'"
        $graph = "$Conform,format=yuv444p," +
            "drawbox=x=ih/16-ih/60:y=ih*0.74:w=ih/220:h=ih*0.105:color=0xFFA238:t=fill:enable='between(t,$s,$e)'," +
            "drawtext=textfile=$(New-TextFile $Text.ToUpperInvariant()):fontfile=font.ttf:fontsize=h/22:fontcolor=white:x=$x" +
            ":y=h*0.745:shadowcolor=black@0.6:shadowx=2:shadowy=2:alpha=$alpha"
        if ($Sub)
        {
            $graph += ",drawtext=textfile=$(New-TextFile $Sub):fontfile=font.ttf:fontsize=h/40:fontcolor=0xD8DEE6:x=$x" +
                ":y=h*0.80:shadowcolor=black@0.6:shadowx=2:shadowy=2:alpha=$alpha"
        }
        $target = Get-OutPath ("{0}-caption.mkv" -f [IO.Path]::GetFileNameWithoutExtension($source))
        Invoke-Ffmpeg (@('-i', $source, '-vf', $graph, '-map', '0:v', '-map', '0:a?', '-c:a', 'flac') + $Mezzanine + @($target))
        Write-Host $target
    }

    'title'
    {
        if (-not $Text) { throw 'title needs -Text.' }
        Initialize-Font
        $length = if ($PSBoundParameters.ContainsKey('Seconds')) { $Seconds } else { 4.0 }
        $d = Format-Num $length
        $inputs = if ($Background)
        {
            @('-stream_loop', '-1', '-i', (Resolve-Input $Background))
        }
        else
        {
            @('-f', 'lavfi', '-i', "color=c=0x07090C:s=$($Width)x$($Height):r=$Fps")
        }
        $bg = if ($Background) { "[0]$Conform,gblur=sigma=14,eq=brightness=-0.18:saturation=0.7" } else { '[0]null' }
        $alpha = "'clip((t-0.35)/0.5,0,1)'"
        $graph = "$bg,format=yuv444p," +
            "drawtext=textfile=$(New-TextFile $Text):fontfile=font.ttf:fontsize=h/7:fontcolor=white:x=(w-tw)/2:y=(h-th)/2-h/22:alpha=$alpha"
        if ($Sub)
        {
            $graph += ",drawtext=textfile=$(New-TextFile $Sub):fontfile=font.ttf:fontsize=h/32:fontcolor=0xFFB050" +
                ":x=(w-tw)/2:y=(h)/2+h/14:alpha='clip((t-0.9)/0.6,0,1)'"
        }
        if ($Note)
        {
            $graph += ",drawtext=textfile=$(New-TextFile $Note):fontfile=font.ttf:fontsize=h/48:fontcolor=0xB8C0CC" +
                ":x=(w-tw)/2:y=h-h/8:alpha='clip((t-1.4)/0.6,0,1)'"
        }
        $graph += ',' + (Get-Placebo (New-Shader 'glitch' @{ FPS = $Fps; AT = 0.45; LENGTH = 0.45; STRENGTH = 0.8 }))
        $graph += ',' + (Get-Placebo (New-Shader 'cinematic' @{ FPS = $Fps; BARS = 0; FADE_IN = 0.3; FADE_OUT = [math]::Max(0.0, $length - 0.6) }))
        $graph += "[v]"
        $slug = ($Text -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
        $target = Get-OutPath "title-$slug.mkv"
        Invoke-Ffmpeg ($inputs + @('-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo', '-filter_complex', $graph,
            '-map', '[v]', '-map', '1:a', '-t', $d, '-c:a', 'flac') + $Mezzanine + @($target))
        Write-Host $target
    }

    'assemble'
    {
        if (-not $Edl) { throw 'assemble needs -Edl (see edl.example.txt).' }
        $edlPath = Resolve-Input $Edl
        $edlDir = Split-Path $edlPath
        $rows = Get-Content $edlPath | Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') }

        # 1. Conform every row to the master's frame, 60 fps, stereo 48 kHz, with silence where a clip has none. Each run
        # writes its segments to a folder of its own, so a run that was stopped cannot write into the next one's files.
        $runDir = Join-Path $Tmp ('assemble-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory $runDir | Out-Null
        $segments = @()
        $index = 0
        foreach ($row in $rows)
        {
            $cells = @($row.Split('|') | ForEach-Object { $_.Trim() })
            while ($cells.Count -lt 5) { $cells += '' }
            $path = $cells[0]
            if (-not [IO.Path]::IsPathRooted($path))
            {
                $path = @((Join-Path $edlDir $path), (Join-Path $Work $path)) | Where-Object { Test-Path $_ } | Select-Object -First 1
                if (-not $path) { throw "EDL row $($index + 1): no such file $($cells[0])" }
            }
            $from = if ($cells[1]) { [double]::Parse($cells[1], $Inv) } else { 0.0 }
            $length = if ($cells[2]) { [double]::Parse($cells[2], $Inv) } else { (Get-Duration $path) - $from }
            $transition = if ($cells[3]) { $cells[3] } else { 'cut' }
            $fade = if ($cells[4]) { [double]::Parse($cells[4], $Inv) } else { 0.4 }
            if ($transition -eq 'cut') { $transition = 'fade'; $fade = 1.0 / $Fps }

            $segment = Join-Path $runDir ('seg-{0:000}.mkv' -f $index)
            $audio = if (Test-Audio $path) { @('-map', '0:a:0') } else { @('-map', '1:a') }
            # The sound is padded to the cut's length -- never further: an unbounded pad keeps ffmpeg writing silence long after
            # the picture ends -- and the segment stops at the picture's last frame, so it is exactly as long as its frames say.
            $len = Format-Num $length
            Invoke-Ffmpeg (@('-ss', (Format-Num $from), '-t', $len, '-i', $path,
                '-f', 'lavfi', '-t', $len, '-i', 'anullsrc=r=48000:cl=stereo',
                '-vf', "$Conform,format=yuv420p", '-af', "aresample=48000,aformat=channel_layouts=stereo,apad=whole_dur=$len",
                '-map', '0:v:0') + $audio + @('-shortest', '-t', $len) + $SegmentCodec + $Bt709 +
                @('-c:a', 'flac', $segment))

            # Timed by its frames, not the container: a container a few milliseconds longer than its picture puts the next
            # crossfade's start past the last frame, and xfade then stops producing any picture at all.
            $frames = [int](& ffprobe -v error -select_streams v:0 -count_packets -show_entries stream=nb_read_packets -of csv=p=0 $segment).Trim()
            $segments += [pscustomobject]@{ Path = $segment; Length = $frames / [double]$Fps; Transition = $transition; Fade = $fade }
            $index++
        }
        if ($segments.Count -eq 0) { throw 'The EDL has no rows.' }

        # 2. Chain them with xfade/acrossfade; each transition overlaps the end of the clip before it.
        $inputs = @()
        foreach ($s in $segments) { $inputs += $Decode + @('-i', $s.Path) }
        # Every input restarts at zero on one time base, whatever its container says: a screen recording's segment can start
        # a few milliseconds in, and xfade measures each offset against the first frame it is handed.
        $graph = @()
        for ($i = 0; $i -lt $segments.Count; $i++)
        {
            $graph += "[$($i):v]settb=1/$($Fps * 1000),setpts=N/($Fps*TB)[s$i]"
            $graph += "[$($i):a]asetpts=PTS-STARTPTS[t$i]"
        }
        $video = '[s0]'
        $sound = '[t0]'
        # Counted in whole frames and written rounded down: an offset and a duration that together reach even a microsecond
        # past the picture so far leave xfade waiting for a frame that never comes, and the picture stops there.
        $offsetFrames = 0
        for ($i = 1; $i -lt $segments.Count; $i++)
        {
            $prev = $segments[$i - 1]
            $fadeFrames = [math]::Max(1, [int][math]::Round($prev.Fade * $Fps))
            $offsetFrames += [int][math]::Round($prev.Length * $Fps) - $fadeFrames
            $fade = ([math]::Floor($fadeFrames / [double]$Fps * 10000) / 10000).ToString('0.0###', $Inv)
            # A millisecond early as well, so the rounding of the transitions before it cannot add up past the frame.
            $start = ([math]::Floor(($offsetFrames / [double]$Fps - 0.001) * 10000) / 10000).ToString('0.0###', $Inv)
            $graph += "$video[s$i]xfade=transition=$($prev.Transition):duration=$($fade):offset=$($start)[v$i]"
            $graph += "$sound[t$i]acrossfade=d=$($fade)[a$i]"
            $video = "[v$i]"
            $sound = "[a$i]"
        }
        $total = ($offsetFrames + [math]::Round($segments[-1].Length * $Fps)) / [double]$Fps

        # 3. Music under the game sound, both faded, then loudness for YouTube (-14 LUFS).
        $mix = "$($sound)volume=$(Format-Num $GameVolume)[game]"
        if ($Music)
        {
            $inputs += @('-stream_loop', '-1', '-i', (Resolve-Input $Music))
            $m = $segments.Count
            $mix += ";[$($m):a]atrim=0:$(Format-Num $total),aresample=48000,aformat=channel_layouts=stereo,volume=$(Format-Num $MusicVolume)," +
                "afade=t=in:d=1.5,afade=t=out:st=$(Format-Num ([math]::Max(0.0, $total - 3.5))):d=3.5[music];" +
                "[game][music]amix=inputs=2:normalize=0[mixed]"
        }
        else { $mix += ';[game]anull[mixed]' }
        $mix += ';[mixed]loudnorm=I=-14:TP=-1.5:LRA=11,aresample=48000[aout]'

        $scaleOut = if ($Uhd) { ',scale=3840:2160:flags=lanczos:out_color_matrix=bt709' } else { '' }
        $graph += "$($video)format=yuv420p$scaleOut[vout]"
        $graph += $mix

        $target = if ($Out) { [IO.Path]::GetFullPath($Out, (Get-Location).Path) } else { Join-Path $Work 'openigi-introduction.mp4' }
        $master = if ($Cpu)
        {
            @('-c:v', 'libx264', '-preset', 'slow', '-crf', $(if ($Uhd) { '17' } else { '15' }), '-profile:v', 'high', '-g', '30', '-bf', '2')
        }
        else
        {
            # NVENC at a constant quality YouTube re-encodes cleanly from, held under a ceiling so a grainy shot cannot balloon.
            $rate = @('-b:v', '60M', '-maxrate', '120M', '-bufsize', '240M')
            @('-c:v', 'h264_nvenc', '-preset', 'p7', '-tune', 'hq', '-rc', 'vbr', '-cq', '17', '-profile:v', 'high', '-g', '30', '-bf', '2') + $rate
        }
        Invoke-Ffmpeg ($inputs + @('-filter_complex', ($graph -join ';'), '-map', '[vout]', '-map', '[aout]') + $master +
            @('-pix_fmt', 'yuv420p') + $Bt709 + @('-c:a', 'aac', '-b:a', '384k', '-movflags', '+faststart', $target))
        Remove-Item -Recurse -Force $runDir
        Write-Host ("{0}  ({1:0.0} s)" -f $target, $total)
    }
}
