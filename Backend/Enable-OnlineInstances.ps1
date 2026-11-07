param([string]$Address, [ValidateRange(1,65535)][int]$Port = 7979, [string]$ServerName)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Address)) {
    throw 'Pass -Address with the direct game IP or a DNS-only hostname. HTTP/CDN proxies do not forward game UDP.'
}
$Address = $Address.Trim()
# The transport address may be an IP while the existing DTLS certificate names a domain.
$ServerName = if ([string]::IsNullOrWhiteSpace($ServerName)) { $Address } else { $ServerName.Trim() }
$projectRoot = Split-Path $PSScriptRoot -Parent
$marker = Join-Path $projectRoot 'Build/MoonServer/moon-network-protocol.txt'
if (!(Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw).Trim() -ne '2') { throw 'Build the P2P/VPS dedicated server before enabling party instances.' }
$configPath = Join-Path $PSScriptRoot 'MoonPersistence/appsettings.local.json'
$configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$backup = Join-Path $projectRoot ('LocalData/backups/online-instances-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($identity in @([Security.Principal.WindowsIdentity]::GetCurrent().Name,'SYSTEM','BUILTIN\Administrators')) {
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $backup -AclObject $acl
Copy-Item -LiteralPath $configPath -Destination (Join-Path $backup 'appsettings.local.json')
# Owned old server shuts down through its receipt-flushing path; never kill a port owner.
& (Join-Path $PSScriptRoot 'Stop-GameServer.ps1')
if (Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue) { throw 'The game port is still owned. Leave that process intact and inspect it before enabling instances.' }
$configuration | Add-Member -NotePropertyName OnlineInstances -NotePropertyValue ([pscustomobject]@{
    ProjectRoot=$projectRoot; Executable='Build/MoonServer/MoonServer.exe'; Address=$Address; ServerName=$ServerName; Port=$Port
}) -Force
$configuration | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding utf8
Write-Host 'Party-instance configuration saved. Start/update the backend; maps will start only when a ready squad deploys.'
