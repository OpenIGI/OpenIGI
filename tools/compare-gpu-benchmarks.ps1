param([string] $Root = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/perf'))

$ErrorActionPreference = 'Stop'
function Median([double[]] $values) {
    $ordered = $values | Sort-Object
    if ($ordered.Count % 2) { return $ordered[[int][math]::Floor(($ordered.Count - 1) / 2)] }
    return ($ordered[$ordered.Count / 2 - 1] + $ordered[$ordered.Count / 2]) / 2
}

$rows = foreach ($phase in 'before', 'after') {
    $phasePath = Join-Path $Root $phase
    if (-not (Test-Path $phasePath)) {
        continue
    }

    Get-ChildItem $phasePath -Filter 'm*-run*.json' | ForEach-Object {
        if ($_.BaseName -notmatch '^m(?<mission>\d+)-(?<preset>[^-]+)-run\d+$') { return }
        $sample = Get-Content $_.FullName | ConvertFrom-Json
        [pscustomobject]@{
            Phase = $phase
            Mission = [int]$Matches.mission
            Preset = $Matches.preset
            FPS = [double]$sample.FramesPerSecond
            OnePercentLow = [double]$sample.OnePercentLowFramesPerSecond
            FrameP95 = [double]$sample.P95FrameMilliseconds
            GPU = [double]$sample.GpuTotalMilliseconds
            GPUP95 = [double]$sample.GpuP95Milliseconds
            Stages = $sample.GpuStages
        }
    }
}

$summary = $rows | Group-Object Phase, Mission, Preset | ForEach-Object {
    $group = $_.Group
    $stages = foreach ($stageNumber in 0..18) {
        $available = $group | ForEach-Object {
            $_.Stages | Where-Object { $_.Stage -eq $stageNumber -and $_.Active }
        }
        if ($available.Count -eq 0) { continue }

        [pscustomobject]@{
            Stage = $stageNumber
            Milliseconds = Median ([double[]]$available.Milliseconds)
            P95Milliseconds = Median ([double[]]$available.P95Milliseconds)
            PeakMilliseconds = Median ([double[]]$available.PeakMilliseconds)
        }
    }
    [pscustomobject]@{
        Phase = $group[0].Phase
        Mission = $group[0].Mission
        Preset = $group[0].Preset
        FPS = Median $group.FPS
        OnePercentLow = Median $group.OnePercentLow
        FrameP95 = Median $group.FrameP95
        GPU = Median $group.GPU
        GPUP95 = Median $group.GPUP95
        GpuStages = $stages
    }
}

$summary |
    Sort-Object Mission, Preset, Phase |
    Select-Object Phase, Mission, Preset, FPS, OnePercentLow, FrameP95, GPU, GPUP95 |
    Format-Table -AutoSize
$deltas = foreach ($after in $summary | Where-Object Phase -eq 'after') {
    $before = $summary | Where-Object {
        $_.Phase -eq 'before' -and $_.Mission -eq $after.Mission -and $_.Preset -eq $after.Preset
    } | Select-Object -First 1
    if ($null -eq $before) { continue }

    [pscustomobject]@{
        Mission = $after.Mission
        Preset = $after.Preset
        FPS = $after.FPS - $before.FPS
        OnePercentLow = $after.OnePercentLow - $before.OnePercentLow
        FrameP95 = $after.FrameP95 - $before.FrameP95
        GPU = $after.GPU - $before.GPU
        GPUP95 = $after.GPUP95 - $before.GPUP95
    }
}

[ordered]@{
    Summary = @($summary | Sort-Object Mission, Preset, Phase)
    Deltas = @($deltas)
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Root 'summary.json')
