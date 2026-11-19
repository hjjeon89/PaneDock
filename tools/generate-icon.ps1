#Requires -Version 5.1
<#
.SYNOPSIS
Recreates PaneDock's application icon from vector-like System.Drawing shapes.
.DESCRIPTION
No external image library or downloaded asset is required. Generates an ICO
with 16, 24, 32, 48, 64, 128 and 256-pixel PNG frames, plus a 256px preview.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate-icon.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $PSScriptRoot '..\src\PaneDock.App\Assets'
[void][System.IO.Directory]::CreateDirectory($assetDirectory)

function New-RoundedRect {
    param([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = [float]($radius * 2)
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-Rounded {
    param(
        [System.Drawing.Graphics]$graphics,
        [float]$x, [float]$y, [float]$width, [float]$height,
        [float]$radius, [System.Drawing.Color]$color
    )
    $path = New-RoundedRect $x $y $width $height $radius
    $brush = New-Object System.Drawing.SolidBrush($color)
    try { $graphics.FillPath($brush, $path) }
    finally { $brush.Dispose(); $path.Dispose() }
}

function Get-Color([string]$hex) {
    return [System.Drawing.ColorTranslator]::FromHtml($hex)
}

function Draw-PaneDock([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.ScaleTransform([float]($size / 256.0), [float]($size / 256.0))

        # Rounded blue app mark, distinct on both light and dark taskbars.
        $outer = New-RoundedRect 15 15 226 226 48
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.RectangleF]::new(15, 15, 226, 226),
            (Get-Color '#39B1FF'), (Get-Color '#1653BF'),
            [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        $border = [System.Drawing.Pen]::new((Get-Color '#1146A6'), 3)
        try {
            $graphics.FillPath($gradient, $outer)
            $graphics.DrawPath($border, $outer)
        }
        finally { $gradient.Dispose(); $border.Dispose(); $outer.Dispose() }

        # Four independent dockable explorer panes.
        Fill-Rounded $graphics 47 47 77 77 15 (Get-Color '#FFFFFF')
        Fill-Rounded $graphics 132 47 77 77 15 (Get-Color '#DDF1FF')
        Fill-Rounded $graphics 47 132 77 77 15 (Get-Color '#DDF1FF')
        Fill-Rounded $graphics 132 132 77 77 15 (Get-Color '#FFF0C5')

        # Simple folder on the first pane.
        Fill-Rounded $graphics 61 71 35 15 5 (Get-Color '#1262B7')
        Fill-Rounded $graphics 61 79 49 29 6 (Get-Color '#1262B7')
        # Compact file-list marks in the top-right and bottom-left panes.
        $blue = Get-Color '#388FD7'
        foreach ($row in @(67, 85, 103)) {
            Fill-Rounded $graphics 146 $row 47 7 3.5 $blue
        }
        foreach ($row in @(151, 169, 187)) {
            Fill-Rounded $graphics 61 $row 48 7 3.5 $blue
        }
        # Amber folder for an instantly recognizable file-manager cue.
        Fill-Rounded $graphics 144 161 34 13 4 (Get-Color '#DA8B1F')
        Fill-Rounded $graphics 144 168 54 29 6 (Get-Color '#F3AB36')
        Fill-Rounded $graphics 147 176 48 18 4 (Get-Color '#FFD06B')

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally { $stream.Dispose() }
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

# Legacy Windows icon loaders (including System.Drawing.Icon) expect DIB frames
# at small sizes. PNG frames are retained for large sizes and the PNG preview.
function Convert-ToDibIconFrame([byte[]]$pngBytes, [int]$size) {
    $inputStream = [System.IO.MemoryStream]::new($pngBytes)
    $bitmap = [System.Drawing.Bitmap]::new($inputStream)
    $outputStream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($outputStream)
    try {
        $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
        $imageLength = $size * $size * 4 + $size * $maskStride

        # BITMAPINFOHEADER, followed by bottom-up BGRA pixels and 1-bit XOR mask.
        $writer.Write([uint32]40)
        $writer.Write([int32]$size)
        $writer.Write([int32]($size * 2))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)
        $writer.Write([uint32]$imageLength)
        $writer.Write([int32]0)
        $writer.Write([int32]0)
        $writer.Write([uint32]0)
        $writer.Write([uint32]0)

        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                $writer.Write([byte]$pixel.B)
                $writer.Write([byte]$pixel.G)
                $writer.Write([byte]$pixel.R)
                $writer.Write([byte]$pixel.A)
            }
        }
        for ($y = $size - 1; $y -ge 0; $y--) {
            $mask = [byte[]]::new($maskStride)
            for ($x = 0; $x -lt $size; $x++) {
                if ($bitmap.GetPixel($x, $y).A -eq 0) {
                    $byteIndex = [int][Math]::Floor($x / 8)
                    $mask[$byteIndex] = [byte]($mask[$byteIndex] -bor (0x80 -shr ($x % 8)))
                }
            }
            $writer.Write($mask)
        }

        return $outputStream.ToArray()
    }
    finally {
        $writer.Dispose()
        $bitmap.Dispose()
        $inputStream.Dispose()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $png = Draw-PaneDock $size
    $frames += ,$(if ($size -le 48) {
        Convert-ToDibIconFrame $png $size
    }
    else {
        $png
    })
}

$iconFile = Join-Path $assetDirectory 'PaneDock.ico'
$fileStream = [System.IO.File]::Create($iconFile)
$writer = [System.IO.BinaryWriter]::new($fileStream)
try {
    $writer.Write([uint16]0) # Reserved
    $writer.Write([uint16]1) # Icon
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + $sizes.Count * 16

    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $frame = $frames[$i]
        $writer.Write([byte]($size -band 255)) # 256 is represented by zero
        $writer.Write([byte]($size -band 255))
        $writer.Write([byte]0) # Color count
        $writer.Write([byte]0) # Reserved
        $writer.Write([uint16]1) # Planes
        $writer.Write([uint16]32) # RGBA
        $writer.Write([uint32]$frame.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Length
    }
    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame)
    }
}
finally { $writer.Dispose() }

$previewFile = Join-Path $assetDirectory 'PaneDock.png'
[System.IO.File]::WriteAllBytes($previewFile, [byte[]]$frames[$frames.Count - 1])
Write-Host "Created icon: $iconFile ($($sizes -join ', ') px)"
Write-Host "Created preview: $previewFile"
