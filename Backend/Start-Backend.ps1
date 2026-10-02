param([switch]$Background)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Start-Database.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
$serverConfig = Get-Content -LiteralPath (Join-Path $projectRoot 'moon-server.local.json') -Raw | ConvertFrom-Json
try {
    $health = Invoke-RestMethod -Uri ($serverConfig.BackendUrl.TrimEnd('/') + '/internal/health') -Headers @{ 'X-Moon-Server-Key' = $serverConfig.ServerKey } -TimeoutSec 2
    if ($health.status -eq 'ready') { Write-Host 'Persistence service is already running.'; return }
} catch { }
Push-Location (Join-Path $PSScriptRoot 'MoonPersistence')
try {
    if ($Background) {
        dotnet build MoonPersistence.csproj --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Persistence service build failed.' }
        $serviceDll = Join-Path (Get-Location).Path 'bin/Debug/net10.0/MoonPersistence.dll'
        $serviceProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"' + $serviceDll + '"') -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -RedirectStandardOutput (Join-Path $projectRoot 'LocalData/backend.stdout.log') -RedirectStandardError (Join-Path $projectRoot 'LocalData/backend.stderr.log') -PassThru
        $serviceProcess.Id | Set-Content -LiteralPath (Join-Path $projectRoot 'LocalData/backend.pid')
        Write-Host "Persistence service started in background (PID $($serviceProcess.Id))."
    } else { dotnet run --project MoonPersistence.csproj --no-launch-profile }
}
finally { Pop-Location }
