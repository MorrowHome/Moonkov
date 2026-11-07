param([switch]$InstallPlugin)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$revision = 'b89c792e3612faf2f12cf35bcc56857313a06be3'
$source = Join-Path $repoRoot 'LocalData/EmbeddedIce/libjuice-v1.7.4'
$build = Join-Path $repoRoot 'LocalData/EmbeddedIce/native-build'
if (!(Test-Path -LiteralPath $source)) {
    & git clone --quiet --depth 1 --branch v1.7.4 https://github.com/paullouisageneau/libjuice.git $source
    if ($LASTEXITCODE) { throw 'libjuice download failed.' }
}
$actual = & git -C $source rev-parse HEAD
if ($LASTEXITCODE -or $actual -ne $revision) { throw 'Unexpected libjuice revision.' }
if (& git -C $source status --porcelain) { throw 'libjuice source must remain unmodified.' }

# Import the C++ toolchain into this script process only. No system PATH changes.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsRoot) { throw 'Visual Studio C++ x64 tools are required.' }
$devcmd = Join-Path $vsRoot 'Common7/Tools/VsDevCmd.bat'
$envLines = & cmd.exe /d /s /c "`"$devcmd`" -arch=amd64 -host_arch=amd64 >nul && set"
if ($LASTEXITCODE) { throw 'C++ environment setup failed.' }
foreach ($line in $envLines) {
    if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process') }
}
& cmake -S $PSScriptRoot -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release "-DLIBJUICE_SOURCE=$source"
if ($LASTEXITCODE) { throw 'CMake configure failed.' }
& cmake --build $build --parallel 2
if ($LASTEXITCODE) { throw 'Native build failed.' }
& ctest --test-dir $build --output-on-failure
if ($LASTEXITCODE) { throw 'Native checks failed; plugin was not installed.' }
if ($InstallPlugin) {
    $destination = Join-Path $repoRoot 'Assets/Plugins/EmbeddedIce/Windows/x86_64/moon_ice.dll'
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $build 'moon_ice.dll') -Destination $destination
    Get-FileHash -LiteralPath $destination -Algorithm SHA256 | Select-Object Hash, Path
}
