[CmdletBinding()]
param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\.codex-build'),
    [string]$RenderRoot = (Join-Path $PSScriptRoot '..\.smoke-render'),
    [switch]$AllowTaskbarChange
)

$ErrorActionPreference = 'Stop'
if (-not $AllowTaskbarChange) {
    throw 'This smoke test starts the real dock and briefly applies its configured taskbar mode. Re-run with -AllowTaskbarChange after closing any existing Vela instance.'
}

$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspacePrefix = $workspace.TrimEnd('\') + '\'
$build = [IO.Path]::GetFullPath($BuildDir)
if (-not ($build + '\').StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "BuildDir must stay inside the Vela workspace: $workspace"
}
$exe = Join-Path $build 'Vela.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Build Vela first; missing $exe" }

$existing = @(Get-Process -Name Vela -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    throw 'A Vela process is already running. The smoke test refuses to touch the owner''s dock.'
}

$log = Join-Path $env:LOCALAPPDATA 'Vela\logs\vela.log'
$marker = Join-Path $env:LOCALAPPDATA 'Vela\taskbar-restore.json'
$logOffset = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0L }
$renderBase = [IO.Path]::GetFullPath($RenderRoot)
if (-not ($renderBase + '\').StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "RenderRoot must stay inside the Vela workspace: $workspace"
}
$renderDir = Join-Path $renderBase (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $renderDir -Force | Out-Null

$dock = $null
try {
    $dock = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while (-not $dock.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $log) {
            $tail = Get-Content -LiteralPath $log -Tail 20 -ErrorAction SilentlyContinue
            if ($tail -match "Vela starting \(pid $($dock.Id)\)") { break }
        }
        Start-Sleep -Milliseconds 200
        $dock.Refresh()
    }
    if ($dock.HasExited) { throw "Dock exited during startup with code $($dock.ExitCode)" }

    $render = Start-Process -FilePath $exe -ArgumentList @('--render', ('"' + $renderDir + '"')) -Wait -PassThru -WindowStyle Hidden
    if ($render.ExitCode -ne 0) { throw "Off-screen render failed with code $($render.ExitCode)" }
    if (@(Get-ChildItem -LiteralPath $renderDir -Filter '*.png').Count -eq 0) { throw 'Off-screen render produced no PNG files.' }

    $quit = Start-Process -FilePath $exe -ArgumentList '--quit' -Wait -PassThru -WindowStyle Hidden
    if ($quit.ExitCode -ne 0) { throw "--quit helper failed with code $($quit.ExitCode)" }
    if (-not $dock.WaitForExit(12000)) { throw 'Dock did not exit cleanly within 12 seconds.' }
    if (Test-Path -LiteralPath $marker) { throw "Taskbar restore marker still exists: $marker" }

    $stream = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Position = if ($stream.Length -lt $logOffset) { 0 } else { $logOffset }
        $reader = [IO.StreamReader]::new($stream)
        $newLog = $reader.ReadToEnd()
    }
    finally { if ($reader) { $reader.Dispose() } else { $stream.Dispose() } }
    if ($newLog -notmatch 'Vela stopped cleanly') { throw 'Clean shutdown was not recorded in vela.log.' }
    if ($newLog -match ' ERROR ') { throw 'The smoke-test session wrote an ERROR line to vela.log.' }

    Write-Host "Smoke test passed. Render output: $renderDir"
}
finally {
    if ($dock -and -not $dock.HasExited) {
        try {
            Start-Process -FilePath $exe -ArgumentList '--quit' -Wait -WindowStyle Hidden
            $null = $dock.WaitForExit(8000)
        }
        catch { Write-Warning "Could not request clean shutdown: $($_.Exception.Message)" }
    }
    if (Test-Path -LiteralPath $marker) {
        try { Start-Process -FilePath $exe -ArgumentList '--restore-taskbar' -Wait -WindowStyle Hidden }
        catch { Write-Warning "Emergency taskbar restore failed: $($_.Exception.Message)" }
    }
}
