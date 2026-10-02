param(
    [string]$PostgresBin = 'D:\PostgreSQL\18\bin',
    [int]$Port = 5433
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dataRoot = Join-Path $projectRoot 'LocalData'
$cluster = Join-Path $dataRoot 'postgresql'
$backendConfig = Join-Path $PSScriptRoot 'MoonPersistence\appsettings.local.json'
$serverConfig = Join-Path $projectRoot 'moon-server.local.json'
if ((Test-Path -LiteralPath $cluster) -or (Test-Path -LiteralPath $backendConfig) -or (Test-Path -LiteralPath $serverConfig)) {
    throw 'Local database/config already exists. Setup will not overwrite it. Use Start-Database.ps1 for an existing installation.'
}
if (!(Test-Path -LiteralPath (Join-Path $PostgresBin 'initdb.exe'))) { throw "PostgreSQL programs not found: $PostgresBin" }
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw "Port $Port is occupied; choose a free -Port." }
function New-LocalSecret {
    $bytes = New-Object byte[] 32
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    return [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
}
$adminPassword = New-LocalSecret
$appPassword = New-LocalSecret
$serverKey = New-LocalSecret
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$passwordFile = Join-Path $dataRoot 'postgres-admin.password'
[IO.File]::WriteAllText($passwordFile, $adminPassword, [Text.Encoding]::ASCII)
& (Join-Path $PostgresBin 'initdb.exe') -D $cluster -U moonkov_admin --encoding=UTF8 --auth=scram-sha-256 --pwfile=$passwordFile --no-locale
if ($LASTEXITCODE -ne 0) { throw 'initdb failed. Partial data directory retained for inspection.' }
$databaseConfig = @{ PostgresBin = $PostgresBin; Port = $Port }
$databaseConfig | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'database.local.json') -Encoding UTF8
& (Join-Path $PSScriptRoot 'Start-Database.ps1')
$previousPassword = $env:PGPASSWORD
try {
    $env:PGPASSWORD = $adminPassword
    # Generated hex secrets contain no SQL metacharacters. No secret is put on the command line.
    "CREATE ROLE moonkov_app LOGIN PASSWORD '$appPassword';`nCREATE DATABASE moonkov OWNER moonkov_app;" |
        & (Join-Path $PostgresBin 'psql.exe') -X -h 127.0.0.1 -p $Port -U moonkov_admin -d postgres -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw 'Database provisioning failed. Existing data/config retained.' }
} finally { $env:PGPASSWORD = $previousPassword }
@{
    Urls = 'http://127.0.0.1:5080'
    ConnectionStrings = @{ Postgres = "Host=127.0.0.1;Port=$Port;Database=moonkov;Username=moonkov_app;Password=$appPassword" }
    ServerKey = $serverKey
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $backendConfig -Encoding UTF8
@{
    BackendUrl = 'http://127.0.0.1:5080/'
    ServerKey = $serverKey
    OutboxDirectory = 'LocalData/raid-outbox'
} | ConvertTo-Json | Set-Content -LiteralPath $serverConfig -Encoding UTF8
Write-Host "Project database created on 127.0.0.1:$Port. Existing PostgreSQL services were not modified."
Write-Host 'Next: run Backend/Start-Backend.ps1, then start the game server.'
