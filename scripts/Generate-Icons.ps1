param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\assets'))

Add-Type -AssemblyName System.Drawing
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

function New-CoinIcon([string]$character, [string]$name) {
    $size = 1024
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(39, 25, 58))
    $shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120, 15, 8, 20))
    $coinBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(120, 120)),
        (New-Object System.Drawing.Point(900, 900)),
        [System.Drawing.Color]::FromArgb(255, 231, 119),
        [System.Drawing.Color]::FromArgb(183, 104, 18)
    )
    $rim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 249, 205), 24)
    $innerRim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(117, 62, 16), 16)
    $textShadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(250, 226, 155))
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(76, 35, 13))
    $font = New-Object System.Drawing.Font('Microsoft YaHei', 480, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    try {
        $graphics.FillRectangle($background, 0, 0, $size, $size)
        $graphics.FillEllipse($shadow, 82, 91, 870, 870)
        $graphics.FillEllipse($coinBrush, 68, 63, 870, 870)
        $graphics.DrawEllipse($rim, 82, 77, 842, 842)
        $graphics.DrawEllipse($innerRim, 120, 115, 766, 766)
        $graphics.DrawString($character, $font, $textShadow, (New-Object System.Drawing.RectangleF(4, 36, 1024, 1024)), $format)
        $graphics.DrawString($character, $font, $textBrush, (New-Object System.Drawing.RectangleF(0, 25, 1024, 1024)), $format)
        $png = Join-Path $resolvedOutput "$name.png"
        $ico = Join-Path $resolvedOutput "$name.ico"
        $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        & (Join-Path $PSScriptRoot 'Make-Icon.ps1') -Source $png -Destination $ico
    } finally {
        $format.Dispose(); $font.Dispose(); $textBrush.Dispose(); $textShadow.Dispose()
        $innerRim.Dispose(); $rim.Dispose(); $coinBrush.Dispose(); $shadow.Dispose()
        $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
}

New-CoinIcon '简' 'QuickSimplifyIcon'
New-CoinIcon '繁' 'QuickSimplifyTraditional'
