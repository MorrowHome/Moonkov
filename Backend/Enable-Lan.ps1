param([string]$HostAddress)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$HostAddress) {
    $physicalAdapters = @(Get-NetAdapter -Physical | Where-Object Status -eq Up | Select-Object -ExpandProperty InterfaceIndex)
    $addresses = @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.InterfaceIndex -in $physicalAdapters -and $_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '169.254.*'
    } | Select-Object -ExpandProperty IPAddress)
    if ($addresses.Count -ne 1) { throw 'Specify -HostAddress with the Windows IPv4 address used by the other computers.' }
    $HostAddress = $addresses[0]
}
if (!(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $HostAddress -ErrorAction SilentlyContinue)) {
    throw 'HostAddress must belong to this Windows computer.'
}
$address = [System.Net.IPAddress]::Parse($HostAddress).GetAddressBytes()
if (!($address[0] -eq 10 -or ($address[0] -eq 172 -and $address[1] -ge 16 -and $address[1] -le 31) -or
    ($address[0] -eq 192 -and $address[1] -eq 168))) { throw 'LAN test mode requires a private IPv4 address.' }

& (Join-Path $PSScriptRoot 'Start-Backend.ps1') -Lan -Background
$clientFolder = Join-Path $projectRoot 'LocalData/LanClient'
New-Item -ItemType Directory -Path $clientFolder -Force | Out-Null
@{ AccountServiceUrl = "http://${HostAddress}:5080/"; AllowLanHttp = $true } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $clientFolder 'moon-client.local.json') -Encoding UTF8
Write-Host "Client config: $clientFolder/moon-client.local.json (copy next to the Mac .app)."

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$administrator = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$administrator) {
    Write-Warning 'Backend is listening for LAN clients. Rerun Backend/Enable-Lan.ps1 as administrator to configure the firewall.'
    return
}
# This computer uses a Public network profile. Limit the rules by subnet and local address instead.
foreach ($rule in @(
    @{ Name = 'Moonkov-LAN-Account'; Port = 5080; Protocol = 'TCP' },
    @{ Name = 'Moonkov-LAN-Game'; Port = 7979; Protocol = 'UDP' }
)) {
    $existing = Get-NetFirewallRule -Name $rule.Name -ErrorAction SilentlyContinue
    if ($existing) { Remove-NetFirewallRule -Name $rule.Name }
    New-NetFirewallRule -Name $rule.Name -DisplayName $rule.Name -Direction Inbound -Action Allow -Enabled True -Profile Any `
        -LocalAddress $HostAddress -RemoteAddress LocalSubnet -Protocol $rule.Protocol -LocalPort $rule.Port | Out-Null
}
Write-Host "LAN firewall configured for ${HostAddress}: TCP 5080, UDP 7979, local subnet only."
