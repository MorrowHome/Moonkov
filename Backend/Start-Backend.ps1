param([switch]$Background, [switch]$Lan, [switch]$Update)
$ErrorActionPreference = 'Stop'
$requiredFeatures = @('species-harvest-v2', 'missions-v1', 'recovery-inbox-v1', 'bounty-clash-v1', 'game-join-tickets-v1')
& (Join-Path $PSScriptRoot 'Start-Database.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
$serverConfig = Get-Content -LiteralPath (Join-Path $projectRoot 'moon-server.local.json') -Raw | ConvertFrom-Json
try {
    $health = Invoke-RestMethod -Uri ($serverConfig.BackendUrl.TrimEnd('/') + '/internal/health') -Headers @{ 'X-Moon-Server-Key' = $serverConfig.ServerKey } -TimeoutSec 2
} catch { $health = $null }
if ($health.status -eq 'ready') {
    $port = ([uri]$serverConfig.BackendUrl).Port
    $lanListener = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalAddress -in @('0.0.0.0', '::') }
    if (!$Update -and (!$Lan -or $lanListener)) {
        $missingFeatures = @($requiredFeatures | Where-Object { $health.features -notcontains $_ })
        if ($missingFeatures.Count -gt 0) { throw ('The running backend is outdated (missing: ' + ($missingFeatures -join ', ') + '). Back up production and use -Update to rebuild/restart it.') }
        Write-Host 'Persistence service is already running with the required recovery capabilities. Use -Update to rebuild and restart it.'; return
    }
    # Only stop the project process recorded by our own background launcher.
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot 'LocalData/backend.pid'))) {
        throw 'A service is running without a project PID. Refusing to stop an unowned process.'
    }
    & (Join-Path $PSScriptRoot 'Stop-Backend.ps1')
}
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (!$dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (!(Test-Path -LiteralPath $dotnet)) { throw '.NET SDK was not found.' }
$launchArguments = @()
if ($Lan) {
    $launchArguments = @('--urls', ('http://0.0.0.0:' + ([uri]$serverConfig.BackendUrl).Port))
}
Push-Location (Join-Path $PSScriptRoot 'MoonPersistence')
try {
    if ($Background) {
        & $dotnet build MoonPersistence.csproj --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Persistence service build failed.' }
        $serviceDll = Join-Path (Get-Location).Path 'bin/Debug/net10.0/MoonPersistence.dll'
        $serviceProcess = Start-Process -FilePath $dotnet -ArgumentList (@('"' + $serviceDll + '"') + $launchArguments) -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -RedirectStandardOutput (Join-Path $projectRoot 'LocalData/backend.stdout.log') -RedirectStandardError (Join-Path $projectRoot 'LocalData/backend.stderr.log') -PassThru
        $serviceProcess.Id | Set-Content -LiteralPath (Join-Path $projectRoot 'LocalData/backend.pid')
        $ready = $false
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            if ($serviceProcess.HasExited) { throw 'Persistence service exited. Inspect LocalData/backend.stderr.log.' }
            try {
                $health = Invoke-RestMethod -Uri ($serverConfig.BackendUrl.TrimEnd('/') + '/internal/health') -Headers @{ 'X-Moon-Server-Key' = $serverConfig.ServerKey } -TimeoutSec 2
                if ($health.status -eq 'ready') { $ready = $true; break }
            } catch { }
            Start-Sleep -Milliseconds 250
        }
        if (!$ready) { throw 'Persistence service did not become ready. Inspect LocalData/backend.stderr.log.' }
        $missingFeatures = @($requiredFeatures | Where-Object { $health.features -notcontains $_ })
        if ($missingFeatures.Count -gt 0) { throw ('The running backend is missing required capabilities: ' + ($missingFeatures -join ', ')) }
        Write-Host "Persistence service ready (PID $($serviceProcess.Id)); species harvest, missions and durable extraction recovery enabled."
    } else { & $dotnet run --project MoonPersistence.csproj --no-launch-profile -- @launchArguments }
}
finally { Pop-Location }
