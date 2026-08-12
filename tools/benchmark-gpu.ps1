param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('before', 'after')]
    [string] $Phase,

    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts/perf/$Phase"
$executable = Join-Path $root "apps/OpenIGI.Desktop/bin/$Configuration/net10.0/OpenIGI.Desktop.exe"
New-Item -ItemType Directory -Force -Path $output | Out-Null

if (-not (Test-Path $executable)) {
    dotnet build (Join-Path $root 'apps/OpenIGI.Desktop/OpenIGI.Desktop.csproj') -c $Configuration
}

$adapter = Get-CimInstance Win32_VideoController | Select-Object -First 1 Name, DriverVersion, AdapterRAM
$system = [ordered]@{
    capturedUtc = [DateTime]::UtcNow.ToString('O')
    machine = $env:COMPUTERNAME
    gpu = $adapter
    resolution = '2560x1440'
    presentationHz = 120
    simulationHz = 30
    warmUpFrames = 120
    measuredFrames = 600
    process = $executable
}
$system | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'environment.json')

$missions = 1, 3, 9
$presets = 'vanilla', 'enhanced', 'high', 'ultra'
foreach ($mission in $missions) {
    foreach ($preset in $presets) {
        foreach ($run in 1..3) {
            $json = Join-Path $output "m$mission-$preset-run$run.json"
            Write-Host "Benchmarking mission $mission / $preset / run $run"
            & $executable `
                -mission $mission -fullscreen -size 2560x1440 -freecam -skipcutscene `
                -yaw 0.4 -yawend 3.4 -pitch -0.12 -pitchend -0.12 `
                -benchmark $json +gfx_preset $preset
            if ($LASTEXITCODE -ne 0) {
                throw "Benchmark process failed with exit code $LASTEXITCODE"
            }
        }
    }
}
