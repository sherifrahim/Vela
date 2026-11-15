# Vela dev loop: quit the running dock, build, start it again.
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/dev.ps1 [-Release] [-NoRun]
param([switch]$Release, [switch]$NoRun)
$root = Split-Path $PSScriptRoot -Parent
$cfg = if ($Release) { "Release" } else { "Debug" }
$exe = Join-Path $root "src\Vela\bin\$cfg\net10.0-windows10.0.19041.0\Vela.exe"
if (Get-Process Vela -ErrorAction SilentlyContinue) {
    if (Test-Path $exe) { & $exe --quit } else { Write-Warning "Vela is running from elsewhere; quit it from its right-click menu." }
    Start-Sleep -Seconds 3
}
dotnet build (Join-Path $root "src\Vela\Vela.csproj") -c $cfg -nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not $NoRun) { Start-Process $exe }
