Add-Type -AssemblyName System.Drawing
$exePath = Join-Path $PSScriptRoot "VaultPrintTool.exe"
$pngPath = Join-Path $PSScriptRoot "icon.png"
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exePath)
$bitmap = $icon.ToBitmap()
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
