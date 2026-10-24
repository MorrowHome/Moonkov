$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pidFile = Join-Path $projectRoot 'LocalData/game-server.pid.json'
if (!(Test-Path -LiteralPath $pidFile)) { Write-Host 'No project game server was recorded.'; return }
$record = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
$server = Get-CimInstance Win32_Process -Filter ("ProcessId=" + [int]$record.Pid) -ErrorAction SilentlyContinue
if ($server) {
    if ($server.ExecutablePath -ne $record.Executable -or $server.CommandLine -notmatch '-batchmode') { throw 'The recorded PID is not this project server. Refusing to stop it.' }
    $expected = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Build/MoonServer/MoonServer.exe'))
    if ($server.ExecutablePath -ne $expected) { throw 'Custom server executable detected; stop it explicitly after inspecting the recorded path.' }
    $stopFile = [IO.Path]::GetFullPath([string]$record.StopFile)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'LocalData')) + [IO.Path]::DirectorySeparatorChar
    if (!$stopFile.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($stopFile) -notmatch '^game-server-[a-f0-9]{32}\.stop$') { throw 'Invalid shutdown signal path. Refusing to write it.' }
    'shutdown' | Set-Content -LiteralPath $stopFile
    try { Wait-Process -Id $server.ProcessId -Timeout 30 -ErrorAction Stop }
    catch { throw 'Graceful shutdown is still pending. Process retained so raid journals can finish; inspect LocalData/game-server.log.' }
}
Remove-Item -LiteralPath $pidFile
Write-Host 'Project game server stopped. Existing raid journals are retained for recovery.'
