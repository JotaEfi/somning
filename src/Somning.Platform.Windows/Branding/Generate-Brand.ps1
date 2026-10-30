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
            $graphics.DrawImage($source, $target, 0, 0, $source.Width, $source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
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
