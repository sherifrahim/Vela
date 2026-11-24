# Captures the primary screen (physical pixels) to a PNG. -CropBottom N keeps only the bottom N px (the dock).
# -Secondary captures the first other display instead.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshot.ps1 -Out D:/tmp/shot.png -CropBottom 140
param([string]$Out, [int]$CropBottom = 0, [int]$CropTop = 0, [switch]$Secondary)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System.Runtime.InteropServices;
public static class DpiFix {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr ctx);
}
"@
# Per-monitor aware: physical pixels on every display (the displays use different scales).
if (-not [DpiFix]::SetProcessDpiAwarenessContext([System.IntPtr]-4)) { [DpiFix]::SetProcessDPIAware() | Out-Null }
$screen = [System.Windows.Forms.Screen]::PrimaryScreen
if ($Secondary) { $screen = [System.Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Select-Object -First 1 }
$b = $screen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
if ($CropBottom -gt 0) {
  $rect = New-Object System.Drawing.Rectangle 0, ($b.Height - $CropBottom), $b.Width, $CropBottom
  $bmp = $bmp.Clone($rect, $bmp.PixelFormat)
} elseif ($CropTop -gt 0) {
  $rect = New-Object System.Drawing.Rectangle 0, 0, $b.Width, $CropTop
  $bmp = $bmp.Clone($rect, $bmp.PixelFormat)
}
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"$($b.Width)x$($b.Height)"
