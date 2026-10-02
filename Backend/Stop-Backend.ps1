$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pidFile = Join-Path $projectRoot 'LocalData/backend.pid'
if (!(Test-Path -LiteralPath $pidFile)) { Write-Host 'No background backend PID recorded.'; return }
$backendProcessId = [int](Get-Content -LiteralPath $pidFile -Raw)
$process = Get-CimInstance Win32_Process -Filter "ProcessId=$backendProcessId"
$expectedDll = Join-Path $PSScriptRoot 'MoonPersistence\bin\Debug\net10.0\MoonPersistence.dll'
if ($process -and $process.CommandLine -and $process.CommandLine.IndexOf($expectedDll, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    Stop-Process -Id $backendProcessId
} elseif ($process) {
    throw 'Recorded PID belongs to another process; refusing to stop it.'
}
Remove-Item -LiteralPath $pidFile
Write-Host 'Background persistence service stopped.'
