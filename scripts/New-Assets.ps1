param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($entry in @(@('StoreLogo.png',50), @('SmallLogo.png',44), @('Logo.png',150))) {
    $size = [int]$entry[1]
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(48,48,48))
    $colors = @('#10A586','#D87959','#4385EF')
    for ($i=0; $i -lt 3; $i++) {
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($colors[$i]))
        $height = $size * (0.30 + $i * 0.13)
        $g.FillRectangle($brush, [single]($size*(0.20+$i*0.23)), [single]($size*0.78-$height), [single]($size*0.14), [single]$height)
        $brush.Dispose()
    }
    $bitmap.Save((Join-Path $Destination $entry[0]), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bitmap.Dispose()
}
foreach ($enabled in @($true, $false)) {
    $bitmap = New-Object System.Drawing.Bitmap(68,40)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $color = if ($enabled) { '#4B94E5' } else { '#85858C' }
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($color))
    $g.FillEllipse($brush,0,0,40,40); $g.FillEllipse($brush,28,0,40,40); $g.FillRectangle($brush,20,0,28,40)
    $x = if ($enabled) { 34 } else { 6 }
    $g.FillEllipse([System.Drawing.Brushes]::White,$x,6,28,28)
    $name = if ($enabled) { 'toggle-on.png' } else { 'toggle-off.png' }
    $bitmap.Save((Join-Path $Destination $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $brush.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
