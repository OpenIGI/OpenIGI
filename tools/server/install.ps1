<#
.SYNOPSIS
Installs the OpenIGI dedicated server on Windows, started with the machine, from the folder this script sits in.

.DESCRIPTION
Copies the server to Program Files\OpenIGI Server, writes its settings to ProgramData\OpenIGI\server.cfg (kept if it is
already there), keeps bans and logs beside them, opens its UDP port in Windows Firewall, and registers a scheduled task
that starts it at boot as SYSTEM and restarts it if it stops. The game's data is not part of the server: point -Game at
your own installation's folder.

.EXAMPLE
.\install.ps1
.\install.ps1 -Game 'C:\Games\IGI' -Name 'My server' -Port 26001 -Title igi1 -Rcon secret -Public 1 -Yes
#>
param(
    [string]$Game,
    [string]$Title,
    [string]$Name,
    [string]$Port,
    [string]$MaxPlayers,
    [string]$Rcon,
    [string]$Public,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run it from an administrator PowerShell.'
}

function Ask([string]$Value, [string]$Question, [string]$Default) {
    if ($Value) { return $Value }
    if ($Yes) { return $Default }
    $answer = Read-Host "$Question [$Default]"
    if ($answer) { return $answer } else { return $Default }
}

$Game = Ask $Game "Where is the game's data (your own install's folder)" 'C:\Games\IGI'
$Title = Ask $Title 'Which game: igi1 or igi2' 'igi1'
$Name = Ask $Name "The server's name" 'OpenIGI server'
$Port = Ask $Port 'UDP port' '26001'
$MaxPlayers = Ask $MaxPlayers 'Most players (1-32)' '16'
$Rcon = Ask $Rcon 'Remote console password (empty: none)' ''
$Public = Ask $Public 'List it in the server browser (1/0)' '1'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$prefix = Join-Path $env:ProgramFiles 'OpenIGI Server'
$state = Join-Path $env:ProgramData 'OpenIGI'
$config = Join-Path $state 'server.cfg'

if (-not (Test-Path $Game)) { Write-Warning "$Game does not exist yet. Copy your game's folder there before the server starts." }

New-Item -ItemType Directory -Force $prefix, $state | Out-Null
Copy-Item -Path (Join-Path $here '*') -Destination $prefix -Recurse -Force

if (Test-Path $config) {
    Write-Host "Keeping the settings already in $config."
} else {
    (Get-Content (Join-Path $prefix 'server.cfg.example')) `
        -replace '^name .*', "name `"$Name`"" `
        -replace '^port .*', "port $Port" `
        -replace '^maxplayers .*', "maxplayers $MaxPlayers" `
        -replace '^rcon_password .*', "rcon_password `"$Rcon`"" `
        -replace '^public .*', "public $Public" `
        -replace '^bans .*', "bans `"$(Join-Path $state 'bans.json')`"" `
        -replace '^log .*', "log `"$(Join-Path $state 'server.log')`"" |
        Set-Content -Encoding utf8 $config
}

if (-not (Get-NetFirewallRule -DisplayName 'OpenIGI Server' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'OpenIGI Server' -Direction Inbound -Protocol UDP -LocalPort $Port -Action Allow | Out-Null
}

$exe = Join-Path $prefix 'OpenIGI.Server.exe'
$action = New-ScheduledTaskAction -Execute $exe -Argument "-config `"$config`" -game `"$Game`" -title $Title" -WorkingDirectory $state
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'OpenIGI Server' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName 'OpenIGI Server'

Write-Host ''
Write-Host "The server is running and starts with Windows (Task Scheduler: 'OpenIGI Server')."
Write-Host "Its log:       $(Join-Path $state 'server.log')"
Write-Host "Its settings:  $config, then: Stop-ScheduledTask 'OpenIGI Server'; Start-ScheduledTask 'OpenIGI Server'"
Write-Host "UDP port $Port is open in Windows Firewall; open it on your router too for players outside your network."
