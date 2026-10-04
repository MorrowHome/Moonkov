param([switch]$Background, [switch]$Lan)
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
    if (!$Lan -or $lanListener) { Write-Host 'Persistence service is already running.'; return }
    # Only stop the project process recorded by our own background launcher.
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot 'LocalData/backend.pid'))) {
        throw 'A loopback service is running without a project PID. Stop it manually, then start with -Lan.'
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
        Write-Host "Persistence service started in background (PID $($serviceProcess.Id))."
    } else { & $dotnet run --project MoonPersistence.csproj --no-launch-profile -- @launchArguments }
}
finally { Pop-Location }
