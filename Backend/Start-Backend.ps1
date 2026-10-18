param([switch]$Background, [switch]$Lan, [switch]$Update)
$ErrorActionPreference = 'Stop'
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
    if (!$Update -and (!$Lan -or $lanListener)) { Write-Host 'Persistence service is already running. Use -Update to rebuild and restart it.'; return }
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
        if ($health.features -notcontains 'halo-research-v1') { throw 'The running service does not support biological halo research.' }
        Write-Host "Persistence service ready (PID $($serviceProcess.Id)); alien harvest and permanent halo research enabled."
    } else { & $dotnet run --project MoonPersistence.csproj --no-launch-profile -- @launchArguments }
}
finally { Pop-Location }
