param([string]$Executable = 'Build/MoonServer/MoonServer.exe', [ValidateRange(1,65535)][int]$Port = 7979)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$serverExe = [IO.Path]::GetFullPath([IO.Path]::Combine($projectRoot, $Executable))
if (!(Test-Path -LiteralPath $serverExe)) { throw 'Build the current Windows Dedicated Server first: Tools / Moonkov / Build Local Windows Server.' }
$configPath = Join-Path $projectRoot 'moon-server.local.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$health = Invoke-RestMethod -Uri ($config.BackendUrl.TrimEnd('/') + '/internal/health') -Headers @{ 'X-Moon-Server-Key'=$config.ServerKey } -TimeoutSec 5
if ($health.status -ne 'ready' -or $health.features -notcontains 'game-join-tickets-v1') { throw 'Update/start the account backend before the game server.' }
$pidFile = Join-Path $projectRoot 'LocalData/game-server.pid.json'
if (Test-Path -LiteralPath $pidFile) {
    $record = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
    $existing = Get-CimInstance Win32_Process -Filter ("ProcessId=" + [int]$record.Pid) -ErrorAction SilentlyContinue
    if ($existing -and $existing.ExecutablePath -eq $serverExe) { Write-Host 'The recorded game server is already running.'; return }
}
if (Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue) { throw 'The game UDP port is already owned by another process. Refusing to start another server.' }
$log = Join-Path $projectRoot 'LocalData/game-server.log'
if (Test-Path -LiteralPath $log) {
    Move-Item -LiteralPath $log -Destination (Join-Path $projectRoot ('LocalData/game-server-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.previous.log'))
}
$previousConfig = $env:MOON_SERVER_CONFIG
$previousShutdown = $env:MOON_SERVER_SHUTDOWN_FILE
$shutdownFile = Join-Path $projectRoot ('LocalData/game-server-' + [guid]::NewGuid().ToString('N') + '.stop')
try {
    # Relative certificate/outbox paths remain rooted at the original server config.
    $env:MOON_SERVER_CONFIG = $configPath
    $env:MOON_SERVER_SHUTDOWN_FILE = $shutdownFile
    $server = Start-Process -FilePath $serverExe -ArgumentList @('-batchmode','-nographics','-logFile',('"'+$log+'"'),("port="+$Port)) `
        -WorkingDirectory (Split-Path $serverExe -Parent) -WindowStyle Hidden -PassThru
} finally {
    if ($null -eq $previousConfig) { Remove-Item Env:MOON_SERVER_CONFIG -ErrorAction SilentlyContinue } else { $env:MOON_SERVER_CONFIG=$previousConfig }
    if ($null -eq $previousShutdown) { Remove-Item Env:MOON_SERVER_SHUTDOWN_FILE -ErrorAction SilentlyContinue } else { $env:MOON_SERVER_SHUTDOWN_FILE=$previousShutdown }
}
[pscustomobject]@{ Pid=$server.Id; Executable=$serverExe; StopFile=$shutdownFile; StartedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $pidFile
for ($attempt=0; $attempt -lt 60; $attempt++) {
    $server.Refresh()
    if ($server.HasExited) { throw 'Game server exited. Inspect LocalData/game-server.log.' }
    if ((Get-NetUDPEndpoint -LocalPort $Port -OwningProcess $server.Id -ErrorAction SilentlyContinue) -and
        (Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch 'FPS2 server -> The Game Scene is loaded.' -Quiet)) {
        Write-Host "Dedicated game server loaded the gameplay scene and is listening on UDP $Port (PID $($server.Id)). No client or multiplayer acceptance was started."
        return
    }
    Start-Sleep -Seconds 1
}
throw 'Server has not opened its UDP port yet. Process retained; inspect LocalData/game-server.log before restarting.'
