# Real PostgreSQL/API checks. Creates one disposable account; removes only that account's rows.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$projectRoot = Split-Path $PSScriptRoot -Parent
$config = Get-Content -LiteralPath (Join-Path $projectRoot 'moon-server.local.json') -Raw | ConvertFrom-Json
$base = $config.BackendUrl.TrimEnd('/')
$client = New-Object System.Net.Http.HttpClient
$playerId = $null
function Assert-Equal($actual, $expected, $label) {
    if ($actual -ne $expected) { throw "$label expected $expected, got $actual" }
}
function Send-Api($method, $route, $body, $token, $internal = $false, $expected = 200) {
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($method)), ($base + $route)
    if ($internal) { $request.Headers.Add('X-Moon-Server-Key', $config.ServerKey) }
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
    Send-Api GET '/internal/health' $null $null $false 401
    $null = Send-Api GET '/internal/health' $null $null $true
    $username = 'smoke_' + [Guid]::NewGuid().ToString('N').Substring(0, 16)
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
        $request.Headers.Add('X-Moon-Server-Key', $config.ServerKey)
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
    Write-Host 'PASS: authentication, revocation, persistent stash, idempotency, conflicting receipts, concurrent rewards, death loss.'
} finally {
    $client.Dispose()
    if ($playerId) {
        $database = Get-Content -LiteralPath (Join-Path $projectRoot 'LocalData/database.local.json') -Raw | ConvertFrom-Json
        $previousPassword = $env:PGPASSWORD
        try {
            $env:PGPASSWORD = [IO.File]::ReadAllText((Join-Path $projectRoot 'LocalData/postgres-admin.password'))
            $id = $playerId.ToString('D')
            "BEGIN; DELETE FROM login_sessions WHERE player_id = '$id'; DELETE FROM raid_settlements WHERE player_id = '$id'; DELETE FROM accounts WHERE player_id = '$id'; DELETE FROM stashes WHERE player_id = '$id'; DELETE FROM players WHERE id = '$id'; COMMIT;" |
                & (Join-Path $database.PostgresBin 'psql.exe') -X -h 127.0.0.1 -p $database.Port -U moonkov_admin -d moonkov -v ON_ERROR_STOP=1 -q
            if ($LASTEXITCODE -ne 0) { Write-Warning 'Smoke-test account cleanup failed.' }
        } finally { $env:PGPASSWORD = $previousPassword }
    }
}
