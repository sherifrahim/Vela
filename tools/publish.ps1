# Produces a self-contained, ReadyToRun build in ./publish (no .NET install needed on the target PC).
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1 [-Rid win-x64|win-arm64]
param([string]$Rid = "win-x64")
$root = Split-Path $PSScriptRoot -Parent
dotnet publish (Join-Path $root "src\Vela\Vela.csproj") -c Release -r $Rid --self-contained true `
    -p:PublishReadyToRun=true -p:PublishSingleFile=false -o (Join-Path $root "publish\$Rid") -nologo
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") (Join-Path $root "publish\$Rid") -Force
Copy-Item (Join-Path $root "src\Vela\Assets\Fonts\OFL.txt") (Join-Path $root "publish\$Rid\Inter-OFL.txt") -Force
