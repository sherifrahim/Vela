# Captures the primary screen (physical pixels) to a PNG. -CropBottom N keeps only the bottom N px (the dock).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshot.ps1 -Out D:/tmp/shot.png -CropBottom 140
param([string]$Out, [int]$CropBottom = 0, [int]$CropTop = 0)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System.Runtime.InteropServices;
public static class DpiFix { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[DpiFix]::SetProcessDPIAware() | Out-Null
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
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
