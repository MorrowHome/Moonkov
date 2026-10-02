$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dataRoot = Join-Path $projectRoot 'LocalData'
$config = Get-Content -LiteralPath (Join-Path $dataRoot 'database.local.json') -Raw | ConvertFrom-Json
& (Join-Path $config.PostgresBin 'pg_ctl.exe') stop -D (Join-Path $dataRoot 'postgresql') -m fast -w
if ($LASTEXITCODE -ne 0) { throw 'Project PostgreSQL could not be stopped.' }
