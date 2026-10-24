param([switch]$ConfigurationOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$checks = New-Object 'System.Collections.Generic.List[object]'
function Record([string]$Name, [bool]$Passed, [string]$Detail) { $checks.Add([pscustomobject]@{ Check=$Name; Passed=$Passed; Detail=$Detail }) }
$client = Get-Content -LiteralPath (Join-Path $projectRoot 'moon-client.local.json') -Raw | ConvertFrom-Json
$server = Get-Content -LiteralPath (Join-Path $projectRoot 'moon-server.local.json') -Raw | ConvertFrom-Json
$serverName = if ($client.GameServerName) { $client.GameServerName } else { $client.ServerAddress }
$ca = Join-Path $projectRoot 'moon-game-ca.pem'
$certificate = [IO.Path]::Combine($projectRoot, $(if ($server.GameCertificateFile) { $server.GameCertificateFile } else { 'LocalData/game-tls/server.pem' }))
$privateKey = [IO.Path]::Combine($projectRoot, $(if ($server.GamePrivateKeyFile) { $server.GamePrivateKeyFile } else { 'LocalData/game-tls/server-key.pem' }))
Record 'Public account HTTPS' (([uri]$client.AccountServiceUrl).Scheme -eq 'https') 'Remote accounts require HTTPS.'
Record 'Server persistence identity' ($server.ServerKey.Length -ge 32) 'Required; no session-only online fallback.'
Record 'DTLS files' ((Test-Path -LiteralPath $ca) -and (Test-Path -LiteralPath $certificate) -and (Test-Path -LiteralPath $privateKey)) 'Public CA, server certificate and private key.'
$git = Get-Command git
$openssl = Join-Path (Split-Path (Split-Path $git.Source -Parent) -Parent) 'usr/bin/openssl.exe'
$command = Get-Command openssl -ErrorAction SilentlyContinue
if ($command) { $openssl = $command.Source }
if ((Test-Path -LiteralPath $openssl) -and (Test-Path -LiteralPath $certificate) -and (Test-Path -LiteralPath $ca)) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $openssl verify -CAfile $ca -verify_hostname $serverName $certificate 2>$null | Out-Null
        Record 'DTLS trust and hostname' ($LASTEXITCODE -eq 0) 'Server certificate validates against the distributed public CA.'
        & $openssl x509 -checkend 2592000 -noout -in $certificate 2>$null | Out-Null
        Record 'DTLS certificate lifetime' ($LASTEXITCODE -eq 0) 'At least 30 days remain.'
        if (Test-Path -LiteralPath $privateKey) {
            $certificateModulus = & $openssl x509 -noout -modulus -in $certificate 2>$null
            $certificateReadOk = $LASTEXITCODE -eq 0
            $keyModulus = & $openssl rsa -noout -modulus -in $privateKey 2>$null
            Record 'DTLS private key matches' ($certificateReadOk -and $LASTEXITCODE -eq 0 -and $certificateModulus -eq $keyModulus) 'Compared without printing key material.'
        }
    } finally { $ErrorActionPreference = $previousPreference }
} else { Record 'DTLS certificate verification' $false 'OpenSSL or certificate is missing.' }
if (!$ConfigurationOnly) {
    try {
        $health = Invoke-RestMethod -Uri ($server.BackendUrl.TrimEnd('/') + '/internal/health') -Headers @{ 'X-Moon-Server-Key'=$server.ServerKey } -TimeoutSec 5
        $required = @('species-harvest-v2','missions-v1','recovery-inbox-v1','bounty-clash-v1','game-join-tickets-v1')
        $missing = @($required | Where-Object { $health.features -notcontains $_ })
        Record 'Authenticated backend health' ($health.status -eq 'ready' -and $missing.Count -eq 0) 'Backend must be running with all current capabilities.'
    } catch { Record 'Authenticated backend health' $false 'Backend unavailable or rejected the configured server identity.' }
    $status = 0
    try { $status = [int](Invoke-WebRequest -Uri ($client.AccountServiceUrl.TrimEnd('/') + '/auth/me') -UseBasicParsing -TimeoutSec 8).StatusCode }
    catch { if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode } }
    Record 'Public HTTPS account route' ($status -eq 401) ("Unauthenticated /auth/me must return 401; observed " + $status + '. Does not test UDP.')
    $port = if ($client.ServerPort) { [int]$client.ServerPort } else { 7979 }
    $listeners = @(Get-NetUDPEndpoint -LocalPort $port -ErrorAction SilentlyContinue)
    Record 'Local game UDP listener' ($listeners.Count -gt 0) 'A listener does not prove public forwarding or gameplay.'
}
$report = [pscustomobject]@{
    TimeUtc=[DateTime]::UtcNow.ToString('o'); ConfigurationOnly=[bool]$ConfigurationOnly
    Passed=@($checks | Where-Object { !$_.Passed }).Count -eq 0
    Checks=$checks.ToArray()
    MultiplayerAcceptance='NOT RUN: requires an explicitly authorized external-client acceptance run.'
}
$reportFile = Join-Path $projectRoot 'LocalData/online-preflight.json'
New-Item -ItemType Directory -Path (Split-Path $reportFile -Parent) -Force | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFile -Encoding UTF8
$checks | Format-Table -AutoSize
Write-Host 'Report: LocalData/online-preflight.json. No services started, no migrations or account mutations performed.'
if (!$report.Passed) { exit 1 }
