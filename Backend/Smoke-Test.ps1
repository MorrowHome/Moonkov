# Real PostgreSQL/API checks. Requires a fresh, explicitly provisioned disposable database.
param([string]$IsolatedDatabase)
$ErrorActionPreference = 'Stop'
if ($IsolatedDatabase -cnotmatch '^moon_smoke_check_[0-9a-f]{32}$') {
    throw 'Use -IsolatedDatabase moon_smoke_check_<32 lowercase hex digits> with a fresh empty test database. See Docs/BackendChecks.md. Project/production databases are refused.'
}
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Data
$projectRoot = Split-Path $PSScriptRoot -Parent
$backendRoot = Join-Path $PSScriptRoot 'MoonPersistence'
$config = Get-Content -LiteralPath (Join-Path $backendRoot 'appsettings.local.json') -Raw | ConvertFrom-Json
# Kestrel endpoints override Urls; refuse them before any database contact or service launch.
# Also reject inherited content-root redirects that could load unchecked appsettings files.
foreach ($entry in Get-ChildItem Env:) {
    if ($entry.Name -imatch '^(?:(?:ASPNETCORE|DOTNET)_)?Kestrel(?::|__|$)' -or
        $entry.Name -imatch '^(?:(?:ASPNETCORE|DOTNET)_)?ContentRoot$') {
        throw 'Smoke checks refuse inherited Kestrel/content-root overrides. Use a clean test shell.'
    }
}
foreach ($file in Get-ChildItem -LiteralPath $backendRoot -Filter 'appsettings*.json' -File) {
    $candidate = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if (@($candidate.PSObject.Properties.Name | Where-Object { $_ -imatch '^Kestrel(?::|$)' }).Count) {
        throw 'Smoke checks require appsettings files without Kestrel endpoint overrides.'
    }
}
$database = Get-Content -LiteralPath (Join-Path $projectRoot 'LocalData/database.local.json') -Raw | ConvertFrom-Json
$source = New-Object System.Data.Common.DbConnectionStringBuilder
$source.ConnectionString = $config.ConnectionStrings.Postgres
if ($source['Host'] -notin @('127.0.0.1', 'localhost', '::1')) { throw 'Smoke checks require a loopback PostgreSQL host.' }
$port = [int]$source['Port']
if ($port -lt 1 -or $port -gt 65535 -or $port -ne [int]$database.Port) { throw 'Local PostgreSQL ports must match.' }
if (!$source['Username'] -or !$source['Password']) { throw 'Local app Username and Password are required.' }
# Build a fresh connection string: no source database name or host alias can override isolation.
$settings = New-Object System.Data.Common.DbConnectionStringBuilder
$settings['Host'] = '127.0.0.1'; $settings['Port'] = $port; $settings['Database'] = $IsolatedDatabase
$settings['Username'] = $source['Username']; $settings['Password'] = $source['Password']; $settings['Pooling'] = $false
$psql = Join-Path $database.PostgresBin 'psql.exe'
if (!(Test-Path -LiteralPath $psql)) { throw 'Local psql.exe was not found.' }
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
function Invoke-TestSql([string]$sql, [string[]]$variables = @()) {
    $previousPassword = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = [string]$settings['Password']
        $output = $sql | & $psql -X -w -h 127.0.0.1 -p $port -U $settings['Username'] -d $IsolatedDatabase -v ON_ERROR_STOP=1 -A -t -q @variables
        if ($LASTEXITCODE -ne 0) { throw 'Isolated smoke-test SQL failed.' }
        return $output
    } finally { $env:PGPASSWORD = $previousPassword }
}
# No schema initialization or API writes until the explicit test database passes these guards.
$preflight = @'
SELECT CASE WHEN current_database() = :'expected_database'
    AND pg_get_userbyid(d.datdba) = current_user
    AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema')
    AND NOT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid())
    THEN 'isolated_empty' ELSE 'refused' END
FROM pg_database d WHERE d.datname = current_database();
'@
if ((Invoke-TestSql $preflight @('-v', "expected_database=$IsolatedDatabase")) -ne 'isolated_empty') {
    throw 'Test database must be empty, owned by the configured app role, and unused by other connections.'
}
& $dotnet build (Join-Path $backendRoot 'MoonPersistence.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Persistence service build failed.' }
$serviceDll = Join-Path $backendRoot 'bin/Debug/net10.0/MoonPersistence.dll'
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
try { $servicePort = $listener.LocalEndpoint.Port } finally { $listener.Stop() }
$base = "http://127.0.0.1:$servicePort"
$serverKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $dotnet; $start.Arguments = '"' + $serviceDll + '"'
$start.WorkingDirectory = $backendRoot; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
$start.EnvironmentVariables['ConnectionStrings__Postgres'] = $settings.ConnectionString
$start.EnvironmentVariables['ServerKey'] = $serverKey; $start.EnvironmentVariables['Urls'] = $base
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.UseProxy = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
$service = $null; $playerId = $null; $username = $null; $testFailure = $null
$cleanupFailures = @()
function Assert-Equal($actual, $expected, $label) {
    if ($actual -ne $expected) { throw "$label expected $expected, got $actual" }
}
function Send-Api($method, $route, $body, $token, $internal = $false, $expected = 200) {
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($method)), ($base + $route)
    if ($internal) { $request.Headers.Add('X-Moon-Server-Key', $serverKey) }
    if ($token) { $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token) }
    if ($null -ne $body) { $request.Content = [System.Net.Http.StringContent]::new(($body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json') }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            Assert-Equal ([int]$response.StatusCode) $expected $route
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($text -and $expected -eq 200) { return $text | ConvertFrom-Json }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
try {
    $service = [System.Diagnostics.Process]::Start($start)
    $stdout = $service.StandardOutput.ReadToEndAsync(); $stderr = $service.StandardError.ReadToEndAsync()
    $ready = $false
    for ($i = 0; $i -lt 100 -and !$service.HasExited; $i++) {
        try { $health = Send-Api GET '/internal/health' $null $null $true; $ready = $health.status -eq 'ready' }
        catch { $ready = $false }
        if ($ready) { break }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready -or $service.HasExited) { throw 'Isolated smoke-test backend did not become ready.' }
    $username = 'smoke_' + [Guid]::NewGuid().ToString('N').Substring(0, 16)
    Send-Api GET '/internal/health' $null $null $false 401
    $null = Send-Api GET '/internal/health' $null $null $true
    $credentials = @{ Username = $username; Password = [Guid]::NewGuid().ToString('N') }
    $registered = Send-Api POST '/auth/register' $credentials $null
    $profile = Send-Api POST '/internal/sessions/resolve' @{ Token = $registered.token } $null $true
    $playerId = [Guid]::Parse($profile.playerId)
    Assert-Equal $profile.dust 0 'new stash'
    Send-Api POST '/auth/login' @{ Username = $username; Password = 'wrong_password' } $null $false 401
    Send-Api POST '/auth/register' $credentials $null $false 409
    $login = Send-Api POST '/auth/login' $credentials $null
    $receipt = @{ PlayerId = $playerId.ToString(); SettlementId = [Guid]::NewGuid().ToString(); Outcome = 'Extracted'; Dust = 2; Alloy = 1; Cells = 1 }
    $first = Send-Api POST '/internal/settlements' $receipt $null $true
    $duplicate = Send-Api POST '/internal/settlements' $receipt $null $true
    Assert-Equal $duplicate.dust 2 'idempotent reward'
    Assert-Equal $duplicate.alloy 1 'idempotent alloy'
    $receipt.Dust = 3
    Send-Api POST '/internal/settlements' $receipt $null $true 409
    # Two simultaneous valid raids must add, never overwrite each other.
    $requests = @(); $tasks = @()
    foreach ($quantity in @(1, 2)) {
        $payload = @{ PlayerId = $playerId.ToString(); SettlementId = [Guid]::NewGuid().ToString(); Outcome = 'Extracted'; Dust = $quantity; Alloy = 0; Cells = 0 }
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, ($base + '/internal/settlements'))
        $request.Headers.Add('X-Moon-Server-Key', $serverKey)
        $request.Content = [System.Net.Http.StringContent]::new(($payload | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
        $requests += $request; $tasks += $client.SendAsync($request)
    }
    for ($i = 0; $i -lt $tasks.Count; $i++) {
        $response = $tasks[$i].GetAwaiter().GetResult()
        try { Assert-Equal ([int]$response.StatusCode) 200 'parallel settlement' }
        finally { $response.Dispose(); $requests[$i].Dispose() }
    }
    $death = @{ PlayerId = $playerId.ToString(); SettlementId = [Guid]::NewGuid().ToString(); Outcome = 'Dead'; Dust = 4; Alloy = 3; Cells = 2 }
    $afterDeath = Send-Api POST '/internal/settlements' $death $null $true
    Assert-Equal $afterDeath.dust 5 'death gives no reward'
    Send-Api POST '/auth/logout' $null $login.token $false 204
    Send-Api GET '/auth/me' $null $login.token $false 401
    Send-Api POST '/internal/sessions/resolve' @{ Token = $login.token } $null $true 401
    $relogin = Send-Api POST '/auth/login' $credentials $null
    $reconnected = Send-Api GET '/auth/me' $null $relogin.token
    Assert-Equal $reconnected.playerId $playerId.ToString() 'stable account'
    Assert-Equal $reconnected.dust 5 'stash survives reconnect'
    Assert-Equal $reconnected.alloy 1 'stash survives reconnect alloy'
    Assert-Equal $reconnected.cells 1 'stash survives reconnect cells'
} catch { $testFailure = $_ }
finally {
    $client.Dispose()
    # Stop only the process created above before removing rows, including on assertion failure.
    if ($service) {
        try {
            if (!$service.HasExited) { $service.Kill() }
            $service.WaitForExit()
            $null = $stdout.GetAwaiter().GetResult(); $null = $stderr.GetAwaiter().GetResult()
        } catch { $cleanupFailures += "Stopping isolated backend failed: $($_.Exception.Message)" }
        finally { $service.Dispose() }
    }
    if ($username) {
        try {
            # Resolve by this run's unique username if registration succeeded but profile lookup failed.
            $id = if ($playerId) { $playerId.ToString('D') } else { '' }
            $sql = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Smoke-Test.Cleanup.sql') -Raw
            $null = Invoke-TestSql $sql @('-v', "expected_database=$IsolatedDatabase", '-v', "username=$username", '-v', "player_id=$id")
        } catch { $cleanupFailures += "Account cleanup failed: $($_.Exception.Message)" }
    }
}
if ($cleanupFailures.Count) {
    if ($testFailure) { Write-Warning ("Smoke checks also failed: " + $testFailure.Exception.Message) }
    throw ($cleanupFailures -join ' ')
}
if ($testFailure) { throw $testFailure }
Write-Host 'PASS: authentication, revocation, persistent stash, idempotency, conflicting receipts, concurrent rewards, death loss; isolated account cleanup completed.'
