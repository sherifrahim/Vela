# Vela dev loop: quit the running dock, build, start it again.
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/dev.ps1 [-Release] [-NoRun]
param([switch]$Release, [switch]$NoRun)
$root = Split-Path $PSScriptRoot -Parent
$cfg = if ($Release) { "Release" } else { "Debug" }
$exe = Join-Path $root "src\Vela\bin\$cfg\net10.0-windows10.0.19041.0\Vela.exe"
$running = Get-Process Vela -ErrorAction SilentlyContinue
if ($running) {
    if (Test-Path $exe) { & $exe --quit } else { Write-Warning "Vela is running from elsewhere; quit it from its right-click menu." }
    # Wait until it has really exited (it restores the taskbar on the way out); a fixed sleep raced with slow exits
    # and the new copy then quit itself as a "second instance", leaving no dock at all.
    $running | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
    if (Get-Process Vela -ErrorAction SilentlyContinue) { Write-Warning "Vela is still running after 30 s; not restarting it."; exit 1 }
}
dotnet build (Join-Path $root "src\Vela\Vela.csproj") -c $cfg -nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not $NoRun) { Start-Process $exe -ArgumentList '--wait-for-exit' }
