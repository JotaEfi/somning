# Export the approved S onda artwork without redrawing or changing its appearance.
param([string]$OutputDirectory = $PSScriptRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PSScriptRoot 'Sources/s-onda-512.png'
$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$payloads = @()
try {
    if ($source.Width -ne 512 -or $source.Height -ne 512) {
        throw 'The approved source must be the transparent 512 x 512 PNG.'
    }
    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $pngBytes = [System.IO.File]::ReadAllBytes($sourcePath)
    [System.IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'somning-logo.png'), $pngBytes)
    $encoded = [Convert]::ToBase64String($pngBytes)
    $svg = '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512"><title>Somning - S onda</title><image width="512" height="512" href="data:image/png;base64,' + $encoded + '"/></svg>'
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'somning-logo.svg'), $svg, [System.Text.UTF8Encoding]::new($false))

    # Tight alpha bounds enlarge the mark in shell icons without changing the approved PNG.
    $left = $source.Width; $top = $source.Height; $right = 0; $bottom = 0
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x, $y).A -gt 24) {
                $left = [Math]::Min($left, $x); $top = [Math]::Min($top, $y)
                $right = [Math]::Max($right, $x); $bottom = [Math]::Max($bottom, $y)
            }
        }
    }
    $edge = [Math]::Max($right - $left + 1, $bottom - $top + 1) + 8
    $cropX = [int][Math]::Floor(($left + $right + 1 - $edge) / 2.0)
    $cropY = [int][Math]::Floor(($top + $bottom + 1 - $edge) / 2.0)
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = $null
        $attributes = $null
        $stream = $null
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
            $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $target = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
            $graphics.DrawImage($source, $target, $cropX, $cropY, $edge, $edge, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
            $stream = [System.IO.MemoryStream]::new()
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $payloads += ,$stream.ToArray()
        } finally {
            if ($stream) { $stream.Dispose() }
            if ($attributes) { $attributes.Dispose() }
            if ($graphics) { $graphics.Dispose() }
            $bitmap.Dispose()
        }
    }
} finally { $source.Dispose() }

$icoStream = [System.IO.File]::Create((Join-Path $OutputDirectory 'somning.ico'))
$writer = [System.IO.BinaryWriter]::new($icoStream)
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

