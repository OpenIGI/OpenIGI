<#
.SYNOPSIS
    Fetches the NVIDIA Streamline runtime and the DLSS model DLLs into an ignored directory.

.DESCRIPTION
    DLSS, ray reconstruction, frame generation and Reflex reach the engine as a
    set of NVIDIA-signed DLLs that this GPL-3.0 repository does not carry. Players get them as the
    optional "nvidia-streamline" runtime pack from the launcher; a development checkout gets them from
    this script, into third-party/streamline (which .gitignore excludes), where the engine finds them
    by walking up from the build output.

    Their absence is a supported state. The engine says once that they are not there and runs its own
    temporal resolve exactly as it would have -- so a fresh checkout builds, runs and tests green with
    no network access and no NVIDIA hardware.

    Two upstream sources are needed, because since Streamline 2.7.32 the SDK repository holds only
    source:

      NVIDIA-RTX/Streamline release zip   sl.interposer.dll and the plugins (sl.common, sl.dlss,
                                          sl.dlss_d, sl.dlss_g, sl.reflex, sl.pcl),
                                          NvLowLatencyVk.dll
      NVIDIA/DLSS tag                     nvngx_dlss.dll, nvngx_dlssd.dll, nvngx_dlssg.dll

    The second is the actual neural network. Its version moves independently of Streamline's and is
    the thing that decides image quality, so it is pinned here rather than taken from whatever the
    Streamline zip happened to bundle.

    The production binaries land in the destination itself; the development ones (which draw NVIDIA's
    debug overlays, read the plugins' JSON configuration and must never ship) land in its development
    subfolder. Configuration files and logs are kept out of the production folder, because the runtime
    pack is built from it exactly as it stands.

.PARAMETER StreamlineVersion
    The NVIDIA-RTX/Streamline release tag to take the plugin DLLs from.

.PARAMETER DlssVersion
    The NVIDIA/DLSS tag to take the NGX model DLLs from. 310.9.x carries the DLSS 4.5 super-resolution
    presets (L, M) and ray reconstruction's preset F.

.PARAMETER Destination
    Where to unpack. Defaults to third-party/streamline beside the repository root.

.PARAMETER SdkZip
    A Streamline SDK zip already on disk, to unpack instead of downloading it again.

.PARAMETER Force
    Re-fetch even when the destination already holds the requested versions.

.PARAMETER PackDir
    Also lay out the "nvidia-streamline" runtime pack in this folder, emptied first: the production
    DLLs and the licences beside them, and nothing else -- no development build, no configuration, no
    logs, which the release tool refuses in a runtime pack anyway. Works from what is already fetched.
    Publish it with:
        dotnet run --project tools/OpenIGI.Tools.Release -- content publish <PackDir>
            --kind runtime --pack nvidia-streamline --version <v> --title "NVIDIA DLSS"
            --summary "..." --min-game <first build that reads OPENIGI_STREAMLINE_PATH>

.EXAMPLE
    ./tools/fetch-streamline.ps1

.EXAMPLE
    ./tools/fetch-streamline.ps1 -DlssVersion v310.9.1 -Force

.EXAMPLE
    ./tools/fetch-streamline.ps1 -PackDir ../pack/nvidia-streamline
#>
[CmdletBinding()]
param(
    [string]$StreamlineVersion = 'v2.14.1',
    [string]$DlssVersion = 'v310.9.1',
    [string]$Destination,
    [string]$SdkZip,
    [switch]$Force,
    [string]$PackDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Destination) {
    $Destination = Join-Path $repoRoot 'third-party/streamline'
}

# The stamp records what is already unpacked. Without it every run that wants to be sure would
# re-download a quarter of a gigabyte of signed binaries to discover nothing had changed.
$stampPath = Join-Path $Destination 'fetched.json'

# The runtime pack as players get it: the production DLLs and their licences, copied from the
# destination. Every DLL must be validly signed, or the engine would refuse the whole folder on every
# player's machine; the pack is not laid out at all rather than laid out wrong.
function Write-RuntimePack([string]$from, [string]$to) {
    $unsigned = @(Get-ChildItem -Path $from -Filter '*.dll' -File |
        Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' })
    if ($unsigned.Count -gt 0) {
        throw "Not laying out the pack: unsigned DLLs in ${from}: $($unsigned.Name -join ', ')"
    }

    if (Test-Path $to) {
        Remove-Item -Path (Join-Path $to '*') -Recurse -Force
    }
    else {
        New-Item -ItemType Directory -Path $to -Force | Out-Null
    }

    Get-ChildItem -Path $from -Filter '*.dll' -File |
        ForEach-Object { Copy-Item -Path $_.FullName -Destination $to -Force }
    $licenceSource = Join-Path $from 'licenses'
    if (Test-Path $licenceSource) {
        Copy-Item -Path $licenceSource -Destination (Join-Path $to 'licenses') -Recurse -Force
    }

    $count = @(Get-ChildItem -Path $to -Recurse -File).Count
    $bytes = (Get-ChildItem -Path $to -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("Runtime pack laid out in {0}: {1} file(s), {2:N0} bytes" -f $to, $count, $bytes)
}

if (-not $Force -and (Test-Path $stampPath)) {
    $have = Get-Content $stampPath -Raw | ConvertFrom-Json
    if ($have.streamline -eq $StreamlineVersion -and $have.dlss -eq $DlssVersion) {
        Write-Host "Streamline $StreamlineVersion + DLSS $DlssVersion already present in $Destination."
        Write-Host 'Pass -Force to re-fetch.'
        if ($PackDir) {
            Write-RuntimePack $Destination $PackDir
        }
        return
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'The GitHub CLI (gh) is required. Install it, run "gh auth login", and try again.'
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("openigi-streamline-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    # ---------------------------------------------------------------- Streamline plugins

    $zipName = "streamline-sdk-$StreamlineVersion.zip"
    if ($SdkZip) {
        $zipPath = (Resolve-Path $SdkZip).Path
        Write-Host "Using $zipPath"
    }
    else {
        Write-Host "Downloading Streamline $StreamlineVersion ..."
        & gh release download $StreamlineVersion --repo NVIDIA-RTX/Streamline --pattern $zipName --dir $staging
        if ($LASTEXITCODE -ne 0) {
            throw "Could not download $zipName from NVIDIA-RTX/Streamline."
        }
        $zipPath = Join-Path $staging $zipName
    }

    $unzipped = Join-Path $staging 'sl'
    Expand-Archive -Path $zipPath -DestinationPath $unzipped -Force

    # The zip's internal layout has moved between releases, so find the production interposer rather than
    # assuming where it sits: the one that is not under a development folder.
    $interposers = Get-ChildItem -Path $unzipped -Filter 'sl.interposer.dll' -Recurse -File
    $production = $interposers | Where-Object { $_.FullName -notmatch '[\\/]development[\\/]' } | Select-Object -First 1
    $development = $interposers | Where-Object { $_.FullName -match '[\\/]development[\\/]' } | Select-Object -First 1
    if (-not $production) {
        throw "No production sl.interposer.dll was found inside $zipName."
    }
    Write-Host "  production plugins in  $($production.Directory.FullName)"
    if ($development) {
        Write-Host "  development plugins in $($development.Directory.FullName)"
    }

    # ---------------------------------------------------------------- NGX models

    Write-Host "Downloading DLSS $DlssVersion ..."
    $ngxDir = Join-Path $staging 'ngx'
    New-Item -ItemType Directory -Path $ngxDir -Force | Out-Null

    # The DLSS repository publishes no release assets; the DLLs live in the tree at a tag. Take the
    # release ("rel") build -- the "dev" DLL draws a watermark over the whole frame.
    #
    # These are tens of megabytes each, well past the 1 MB ceiling at which the contents API stops
    # inlining base64. Ask it only for the raw URL and pull the bytes over HTTP.
    foreach ($model in @('nvngx_dlss.dll', 'nvngx_dlssg.dll', 'nvngx_dlssd.dll')) {
        $apiPath = "repos/NVIDIA/DLSS/contents/lib/Windows_x86_64/rel/$model" + "?ref=$DlssVersion"
        $url = & gh api $apiPath --jq '.download_url'
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($url)) {
            throw "Could not locate $model in NVIDIA/DLSS at $DlssVersion."
        }

        $target = Join-Path $ngxDir $model
        Invoke-WebRequest -Uri $url.Trim() -OutFile $target -UseBasicParsing
        Write-Host ("  {0}  {1:N0} bytes" -f $model, (Get-Item $target).Length)
    }

    # The model licence travels with the models.
    $licenceUrl = & gh api "repos/NVIDIA/DLSS/contents/LICENSE.txt?ref=$DlssVersion" --jq '.download_url'
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($licenceUrl)) {
        Invoke-WebRequest -Uri $licenceUrl.Trim() -OutFile (Join-Path $ngxDir 'DLSS-LICENSE.txt') -UseBasicParsing
    }

    # ---------------------------------------------------------------- Install

    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    # Exactly the modules the engine asks Streamline to load and what they pull in -- nothing else. The
    # engine refuses a folder holding any DLL NVIDIA did not sign, because loading the interposer by path
    # puts its folder on the search path for everything it imports; the extra plugins the zip carries
    # (NIS, DirectSR, the profiler, the overlay) and Microsoft's PIX runtime are therefore left behind.
    $modules = @(
        'sl.interposer.dll', 'sl.common.dll', 'sl.dlss.dll', 'sl.dlss_d.dll', 'sl.dlss_g.dll',
        'sl.reflex.dll', 'sl.pcl.dll', 'NvLowLatencyVk.dll'
    )

    function Copy-Modules([string]$from, [string]$to) {
        foreach ($module in $modules) {
            $source = Join-Path $from $module
            if (Test-Path $source) {
                Copy-Item -Path $source -Destination $to -Force
            }
            else {
                Write-Warning "  $module is not in $from"
            }
        }
        Copy-Item -Path (Join-Path $ngxDir '*.dll') -Destination $to -Force
    }

    # Every Streamline module imports the Visual C++ runtime (msvcp140, vcruntime140, vcruntime140_1). A
    # machine without the redistributable installed could not load any of them, so the runtime travels
    # with the pack, app-locally, as Microsoft permits for these files. Taken from a Visual Studio
    # install's redistributable folder; the engine accepts them only when Microsoft signed them.
    function Find-VcRuntime {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (-not (Test-Path $vswhere)) {
            return $null
        }

        $install = & $vswhere -latest -products * -property installationPath
        if (-not $install) {
            return $null
        }

        Get-ChildItem -Path (Join-Path $install 'VC/Redist/MSVC') -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'x64') -Directory -Filter 'Microsoft.VC14*.CRT' -ErrorAction SilentlyContinue } |
            Select-Object -First 1
    }

    $vcRuntime = Find-VcRuntime
    function Copy-VcRuntime([string]$to) {
        if (-not $vcRuntime) {
            Write-Warning '  The Visual C++ runtime was not found (no Visual Studio install); players without the redistributable will not load the pack.'
            return
        }

        foreach ($file in @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')) {
            Copy-Item -Path (Join-Path $vcRuntime.FullName $file) -Destination $to -Force
        }
    }

    # Production: the DLLs and nothing else. The configuration JSON next to them only has an effect on
    # the development runtime, and leaving it here would put it in the pack.
    Copy-Modules $production.Directory.FullName $Destination
    Copy-VcRuntime $Destination

    if ($development) {
        $devDestination = Join-Path $Destination 'development'
        New-Item -ItemType Directory -Path $devDestination -Force | Out-Null
        Copy-Modules $development.Directory.FullName $devDestination
        Copy-VcRuntime $devDestination

        $pix = Join-Path $development.Directory.FullName 'WinPixEventRuntime.dll'
        if (Test-Path $pix) {
            Copy-Item -Path $pix -Destination $devDestination -Force
        }

        # The development overlay is what makes this runtime worth loading; it is NVIDIA's too.
        $imgui = Join-Path $development.Directory.FullName 'sl.imgui.dll'
        if (Test-Path $imgui) {
            Copy-Item -Path $imgui -Destination $devDestination -Force
        }

        # The plugins' JSON configuration is what the development runtime reads its debug switches from.
        Get-ChildItem -Path $unzipped -Filter 'sl.*.json' -Recurse -File |
            ForEach-Object { Copy-Item -Path $_.FullName -Destination $devDestination -Force }
    }

    # Licences: Streamline's own (MIT) and its third-party notices, the NGX model licence, and whatever
    # per-component licence texts the zip carries.
    $licences = Join-Path $Destination 'licenses'
    New-Item -ItemType Directory -Path $licences -Force | Out-Null
    foreach ($name in @('license.txt', '3rd-party-licenses.md')) {
        $file = Get-ChildItem -Path $unzipped -Filter $name -Recurse -File | Select-Object -First 1
        if ($file) {
            Copy-Item -Path $file.FullName -Destination (Join-Path $licences ("Streamline-" + $file.Name)) -Force
        }
    }
    Get-ChildItem -Path $unzipped -Filter '*.license.txt' -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/]development[\\/]' } |
        ForEach-Object { Copy-Item -Path $_.FullName -Destination $licences -Force }
    if (Test-Path (Join-Path $ngxDir 'DLSS-LICENSE.txt')) {
        Copy-Item -Path (Join-Path $ngxDir 'DLSS-LICENSE.txt') -Destination $licences -Force
    }

    $files = Get-ChildItem -Path $Destination -Filter '*.dll' -File | Sort-Object Name | ForEach-Object {
        [ordered]@{
            name = $_.Name
            size = $_.Length
            sha256 = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    [ordered]@{
        streamline = $StreamlineVersion
        dlss = $DlssVersion
        files = @($files)
    } | ConvertTo-Json -Depth 4 | Set-Content -Path $stampPath -Encoding UTF8

    Write-Host ''
    Write-Host "Installed to $Destination"
    Get-ChildItem -Path $Destination -Filter '*.dll' |
        Sort-Object Name |
        ForEach-Object { Write-Host ("  {0,-28} {1,12:N0} bytes" -f $_.Name, $_.Length) }

    # A signature check here is advisory: the engine verifies every module again before it loads any,
    # because a file that was right when fetched is not necessarily the file that gets loaded later.
    $unsigned = Get-ChildItem -Path $Destination -Filter '*.dll' |
        Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' }
    if ($unsigned) {
        Write-Warning 'These DLLs do not carry a valid signature and the engine will refuse the whole folder:'
        $unsigned | ForEach-Object { Write-Warning "  $($_.Name)" }
    }

    if ($PackDir) {
        Write-RuntimePack $Destination $PackDir
    }
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
