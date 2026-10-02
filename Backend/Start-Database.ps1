$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dataRoot = Join-Path $projectRoot 'LocalData'
$cluster = Join-Path $dataRoot 'postgresql'
$config = Get-Content -LiteralPath (Join-Path $dataRoot 'database.local.json') -Raw | ConvertFrom-Json
$control = Join-Path $config.PostgresBin 'pg_ctl.exe'
& $control status -D $cluster *> $null
if ($LASTEXITCODE -eq 0) { Write-Host 'Project PostgreSQL is already running.'; return }
if (Get-NetTCPConnection -State Listen -LocalPort $config.Port -ErrorAction SilentlyContinue) {
    throw "Port $($config.Port) is occupied by another process."
}
# No system service or interactive window; this instance listens on loopback only.
Start-Process -FilePath (Join-Path $config.PostgresBin 'postgres.exe') -ArgumentList @(
    '-D', ('"' + $cluster + '"'), '-h', '127.0.0.1', '-p', $config.Port
) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $dataRoot 'postgres.stdout.log') -RedirectStandardError (Join-Path $dataRoot 'postgres.stderr.log')
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    & (Join-Path $config.PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p $config.Port *> $null
    if ($LASTEXITCODE -eq 0) { Write-Host "Project PostgreSQL ready on port $($config.Port)."; return }
    Start-Sleep -Milliseconds 250
}
throw 'PostgreSQL did not become ready. Inspect LocalData/postgres.stderr.log.'
