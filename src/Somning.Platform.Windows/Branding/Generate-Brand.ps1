# Rebuild the original Somning mark from vector primitives; no desktop capture or external images.
param([string]$OutputDirectory = $PSScriptRoot)
Add-Type -AssemblyName System.Drawing
function Draw-Mark([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $rect = New-Object System.Drawing.Rectangle(20, 20, 216, 216)
    $body = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 84, 91, 101), [System.Drawing.Color]::FromArgb(255, 14, 18, 24), 90.0)
    $rim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 203, 218, 226), 3.0)
    $wave = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wave.AddBezier(34, 141, 69, 141, 75, 95, 105, 116)
    $wave.AddBezier(105, 116, 129, 133, 129, 166, 153, 139)
    $wave.AddBezier(153, 139, 184, 103, 190, 128, 222, 118)
    $shadow = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, 0, 0, 0), 31.0)
    $light = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 208, 226, 231), 6.0)
    $accent = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 106, 211, 225), 3.0)
    $shine = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, 235, 244, 248), 6.0)
    try {
        $g.FillEllipse($body, $rect)
        $g.DrawEllipse($rim, $rect)
        $g.DrawPath($shadow, $wave)
        $g.DrawPath($light, $wave)
        $g.TranslateTransform(0, 5)
        $g.DrawPath($accent, $wave)
        $g.ResetTransform()
        $g.ScaleTransform($size / 256.0, $size / 256.0)
        $g.DrawArc($shine, 40, 36, 166, 166, 205, 64)
    } finally { $g.Dispose(); $body.Dispose(); $rim.Dispose(); $wave.Dispose(); $shadow.Dispose(); $light.Dispose(); $accent.Dispose(); $shine.Dispose() }
    return $bitmap
}
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$main = Draw-Mark 256
try { $main.Save((Join-Path $OutputDirectory 'somning-logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) } finally { $main.Dispose() }
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$payloads = @()
foreach ($size in $sizes) {
    $bmp = Draw-Mark $size
    $stream = New-Object System.IO.MemoryStream
    try { $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png); $payloads += ,$stream.ToArray() } finally { $bmp.Dispose(); $stream.Dispose() }
}
$icoStream = [System.IO.File]::Create((Join-Path $OutputDirectory 'somning.ico'))
$writer = New-Object System.IO.BinaryWriter($icoStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$payloads[$i].Length); $writer.Write([uint32]$offset)
        $offset += $payloads[$i].Length
    }
    foreach ($payload in $payloads) { $writer.Write([byte[]]$payload) }
} finally { $writer.Dispose(); $icoStream.Dispose() }
