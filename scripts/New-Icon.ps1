<#
.SYNOPSIS
    Draws the OpenLeanPrint icon and writes every size the app and packages need.

.DESCRIPTION
    The icon is a sheet of paper with a folded corner carrying two landscape
    pages, one above the other - which is what 2-up produces and the shortest
    way to say "several pages on one sheet".

    It used to carry a 2x3 grid of pages. Six little rectangles are legible on a
    256-pixel tile and mush together at 16, which is the size that matters most:
    the notification area. Two pages survive being small, and the fold keeps it
    from being a generic square.

    Solid shape, cut-out pages, no outlines. A filled silhouette is what stays
    readable when Windows scales it down and puts it next to the clock.

    Checked in because the previous icon was drawn once by hand and left no way
    to make it again.

.EXAMPLE
    .\New-Icon.ps1
#>
[CmdletBinding()]
param(
    [string]$Repo = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# The app's accent, so the icon and the window agree.
$accent = [System.Drawing.Color]::FromArgb(255, 47, 111, 235)

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # Everything is expressed as a fraction of the canvas, so every size is
        # the same drawing rather than a scaled screenshot of one.
        $u = $size / 32.0
        $left = 3 * $u
        $top = 1.5 * $u
        $width = 26 * $u
        $height = 29 * $u
        $fold = 7 * $u          # the folded corner, top right

        # Sheet with one corner taken off.
        $sheet = New-Object System.Drawing.Drawing2D.GraphicsPath
        $sheet.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($left, $top),
            [System.Drawing.PointF]::new($left + $width - $fold, $top),
            [System.Drawing.PointF]::new($left + $width, $top + $fold),
            [System.Drawing.PointF]::new($left + $width, $top + $height),
            [System.Drawing.PointF]::new($left, $top + $height)
        ))
        $brush = New-Object System.Drawing.SolidBrush($accent)
        $g.FillPath($brush, $sheet)

        # The fold itself, a shade darker so the corner reads as turned over.
        $foldPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $foldPath.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($left + $width - $fold, $top),
            [System.Drawing.PointF]::new($left + $width, $top + $fold),
            [System.Drawing.PointF]::new($left + $width - $fold, $top + $fold)
        ))
        $shade = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 26, 74, 168))
        $g.FillPath($shade, $foldPath)

        # Two landscape pages, cut out of the sheet. Wide, short, and far enough
        # apart to stay two things at 16 pixels.
        $pageLeft = $left + 4 * $u
        $pageWidth = $width - 8 * $u
        $pageHeight = 7 * $u
        $gap = 4 * $u
        $firstTop = $top + $height / 2 - $pageHeight - $gap / 2

        # The offset is worked out first on purpose. Written inline as
        # @(0, $pageHeight + $gap), PowerShell reads it as three elements -
        # 0, the height, and the gap - and the three rectangles that produces
        # overlap into one white box.
        $secondPage = $pageHeight + $gap
        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        foreach ($offset in @(0, $secondPage)) {
            $g.FillRectangle($white, $pageLeft, $firstTop + $offset, $pageWidth, $pageHeight)
        }

        $brush.Dispose(); $shade.Dispose(); $white.Dispose()
        $sheet.Dispose(); $foldPath.Dispose()
    }
    finally { $g.Dispose() }
    return $bmp
}

function Save-Png([int]$width, [int]$height, [string]$path) {
    # Non-square tiles get the square drawing centred on transparency.
    $side = [Math]::Min($width, $height)
    $square = New-IconBitmap $side
    $canvas = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($square, [int](($width - $side) / 2), [int](($height - $side) / 2), $side, $side)
    }
    finally { $g.Dispose() }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    $canvas.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose(); $square.Dispose()
    Write-Host ("  {0,-52} {1}x{2}" -f (Split-Path -Leaf $path), $width, $height)
}

function Save-Ico([int[]]$sizes, [string]$path) {
    # Every image is a PNG inside the .ico, which Windows has understood since
    # Vista and keeps the 256-pixel entry from being enormous.
    $pngs = foreach ($size in $sizes) {
        $bmp = New-IconBitmap $size
        $stream = New-Object System.IO.MemoryStream
        $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    }

    $out = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($out)
    $writer.Write([uint16]0)               # reserved
    $writer.Write([uint16]1)               # type: icon
    $writer.Write([uint16]$pngs.Count)

    $offset = 6 + 16 * $pngs.Count
    foreach ($png in $pngs) {
        $writer.Write([byte]($(if ($png.Size -ge 256) { 0 } else { $png.Size })))
        $writer.Write([byte]($(if ($png.Size -ge 256) { 0 } else { $png.Size })))
        $writer.Write([byte]0)             # palette
        $writer.Write([byte]0)             # reserved
        $writer.Write([uint16]1)           # colour planes
        $writer.Write([uint16]32)          # bits per pixel
        $writer.Write([uint32]$png.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $png.Bytes.Length
    }
    foreach ($png in $pngs) { $writer.Write($png.Bytes) }
    $writer.Flush()

    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    $writer.Dispose(); $out.Dispose()
    Write-Host ("  {0,-52} {1} sizes" -f (Split-Path -Leaf $path), $pngs.Count)
}

Write-Host "Drawing the OpenLeanPrint icon" -ForegroundColor Cyan

Save-Ico @(16, 20, 24, 32, 48, 64, 128, 256) (Join-Path $Repo "src\OpenLeanPrint.App\Assets\OpenLeanPrint.ico")

$tiles = Join-Path $Repo "packaging\Assets"
Save-Png 44 44 (Join-Path $tiles "Square44x44Logo.png")
Save-Png 24 24 (Join-Path $tiles "Square44x44Logo.targetsize-24_altform-unplated.png")
Save-Png 150 150 (Join-Path $tiles "Square150x150Logo.png")
Save-Png 50 50 (Join-Path $tiles "StoreLogo.png")
Save-Png 310 150 (Join-Path $tiles "Wide310x150Logo.png")
Save-Png 150 150 (Join-Path $Repo "docs\images\icon.png")

Write-Host ""
Write-Host "Done. Check the 16-pixel one at its own size - that is the one that has to work." -ForegroundColor Green
