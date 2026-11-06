[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][System.Net.IPAddress]$VpsAddress,
    [Parameter(Mandatory = $true)][System.Net.IPAddress]$SourceAddress,
    [switch]$SessionOnly
)
$ErrorActionPreference = 'Stop'
if ($VpsAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
    $SourceAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
    throw 'Use IPv4 addresses for both the VPS and the physical network adapter.'
}
$source = @(Get-NetIPAddress -IPAddress $SourceAddress.ToString() -AddressFamily IPv4 -ErrorAction Stop |
    Where-Object { $_.AddressState -eq 'Preferred' })
if ($source.Count -ne 1) { throw 'The source IPv4 must belong to exactly one active adapter.' }
$adapter = Get-NetAdapter | Where-Object { $_.ifIndex -eq $source[0].InterfaceIndex }
if (!$adapter.HardwareInterface) { throw 'The source IPv4 must belong to a physical adapter, not a VPN or virtual adapter.' }
$gateway = @((Get-NetIPConfiguration -InterfaceIndex $source[0].InterfaceIndex).IPv4DefaultGateway.NextHop)
if ($gateway.Count -ne 1 -or [string]::IsNullOrWhiteSpace($gateway[0])) { throw 'The physical adapter must have one IPv4 default gateway.' }
$prefix = $VpsAddress.ToString() + '/32'
$ifIndex = $source[0].InterfaceIndex
$active = @(Get-NetRoute -DestinationPrefix $prefix -PolicyStore ActiveStore -ErrorAction SilentlyContinue)
$persistent = @(Get-NetRoute -DestinationPrefix $prefix -PolicyStore PersistentStore -ErrorAction SilentlyContinue)
foreach ($route in @($active) + @($persistent)) {
    if ($route.InterfaceIndex -ne $ifIndex -or $route.NextHop -ne $gateway[0]) {
        throw 'A different route already exists for this VPS. Inspect it before changing it; no route was removed.'
    }
}
if ($active.Count -gt 0) {
    if (!$SessionOnly -and $persistent.Count -eq 0) { throw 'A session route already exists. Use -SessionOnly, or review it before adding persistence.' }
    Write-Host 'The VPS already uses this physical adapter.'
    return
}
$scope = if ($SessionOnly) { 'this Windows session' } else { 'this Windows session and future restarts' }
if ($PSCmdlet.ShouldProcess("$prefix via $($gateway[0]), adapter $($adapter.Name)", "Add one direct route for $scope")) {
    if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script in an Administrator PowerShell. Windows requires elevation to add an IP route.'
    }
    $projectRoot = Split-Path $PSScriptRoot -Parent
    $backupDir = Join-Path $projectRoot 'LocalData/network-routes'
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    @{ Target=$prefix; Source=$SourceAddress.ToString(); InterfaceIndex=$ifIndex; Gateway=$gateway[0];
        Active=@($active | Select-Object DestinationPrefix,InterfaceIndex,NextHop,RouteMetric);
        Persistent=@($persistent | Select-Object DestinationPrefix,InterfaceIndex,NextHop,RouteMetric) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupDir ('vps-before-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')) -Encoding UTF8
    $parameters = @{ DestinationPrefix=$prefix; InterfaceIndex=$ifIndex; NextHop=$gateway[0]; RouteMetric=5; ErrorAction='Stop' }
    # Omitting PolicyStore creates both the active and persistent entry.
    if ($SessionOnly) { $parameters.PolicyStore = 'ActiveStore' }
    New-NetRoute @parameters | Out-Null
    $selected = Find-NetRoute -RemoteIPAddress $VpsAddress.ToString()
    $selected | Select-Object InterfaceAlias,IPAddress,NextHop,DestinationPrefix | ConvertTo-Json -Compress
}
