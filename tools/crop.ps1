# Crops a PNG. Usage: tools/crop.ps1 -In a.png -Out b.png -X 0 -Y 0 -W 400 -H 200
param([string]$In, [string]$Out, [int]$X, [int]$Y, [int]$W, [int]$H)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile($In)
$rect = New-Object System.Drawing.Rectangle $X, $Y, $W, $H
$c = $src.Clone($rect, $src.PixelFormat); $src.Dispose()
$c.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
