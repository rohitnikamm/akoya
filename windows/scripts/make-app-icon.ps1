<#
.SYNOPSIS
  Makes Akoya's Windows app icon, windows/src/Akoya.App/Assets/akoya-app.ico, from the Mac app's icon.

.DESCRIPTION
  The artwork is the Mac's Resources/AppIcon.icns (made by scripts/make-icon.swift): the glowing dot logo on a navy
  rounded square. Sizes 40 px and up are its 1024 px image, cropped so the square fills about 94% of the tile like
  other Windows icons. 16, 20, 24 and 32 px (the taskbar, Start, title bars) are drawn dot by dot instead, because
  shrinking the artwork blurs the dots into a smudge: the same logo (columns of 2-4-6-4-2 dots) in the Mac's colours,
  each size at 8x and downsampled. Every size is stored as PNG. Run on Windows from the repo root:

      powershell -ExecutionPolicy Bypass -File windows/scripts/make-app-icon.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$icns = Join-Path $root 'Resources\AppIcon.icns'
$out = Join-Path $root 'windows\src\Akoya.App\Assets\akoya-app.ico'

# The 1024 px image ('ic10', stored as PNG) from the icns.
function Get-Master {
    $b = [IO.File]::ReadAllBytes($icns)
    $at = 8
    while ($at -lt $b.Length) {
        $type = [Text.Encoding]::ASCII.GetString($b, $at, 4)
        $len = ([int]$b[$at + 4] -shl 24) -bor ([int]$b[$at + 5] -shl 16) -bor ([int]$b[$at + 6] -shl 8) -bor [int]$b[$at + 7]
        if ($type -eq 'ic10') {
            $png = New-Object byte[] ($len - 8)
            [Array]::Copy($b, $at + 8, $png, 0, $len - 8)
            return [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream (, $png)))
        }
        $at += $len
    }
    throw "no 1024 px image (ic10) in $icns"
}

function ConvertTo-Png([System.Drawing.Bitmap]$bitmap) {
    $ms = New-Object IO.MemoryStream
    $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $ms.ToArray()
}

# A large size: the Mac artwork, cropped to 72..952 of 1024 so the rounded square fills about 94%.
function New-FromMaster([System.Drawing.Image]$master, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($master, [System.Drawing.Rectangle]::new(0, 0, $size, $size), [System.Drawing.Rectangle]::new(72, 72, 880, 880),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return ConvertTo-Png $bmp
}

# A small size, drawn: the navy rounded square, then each dot with a soft blue glow, at 8x and downsampled.
# Per size: dot diameter, horizontal and vertical pitch, in final pixels.
$Small = @{ 16 = @(1.5, 2.4, 1.9); 20 = @(1.9, 2.9, 2.35); 24 = @(2.2, 3.4, 2.8); 32 = @(3.0, 4.5, 3.75) }
$Columns = @(2, 4, 6, 4, 2)

function New-Drawn([int]$size) {
    $dot, $px, $py = $Small[$size]
    $S = 8
    $big = New-Object System.Drawing.Bitmap ($size * $S), ($size * $S), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $inset = [math]::Max(0.5, $size * 0.03) * $S
    $w = $size * $S - 2 * $inset
    $r = $w * 0.25
    $square = New-Object System.Drawing.Drawing2D.GraphicsPath
    $square.AddArc($inset, $inset, 2 * $r, 2 * $r, 180, 90)
    $square.AddArc($inset + $w - 2 * $r, $inset, 2 * $r, 2 * $r, 270, 90)
    $square.AddArc($inset + $w - 2 * $r, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $square.AddArc($inset, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $square.CloseFigure()
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF $inset, $inset, $w, $w),
        ([System.Drawing.Color]::Black), ([System.Drawing.Color]::Black), 90
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(255, 15, 30, 70), [System.Drawing.Color]::FromArgb(255, 6, 10, 24),
        [System.Drawing.Color]::FromArgb(255, 22, 44, 90))
    $blend.Positions = @([single]0, [single]0.55, [single]1)
    $fill.InterpolationColors = $blend
    $g.FillPath($fill, $square)

    $tallest = ($Columns | Measure-Object -Maximum).Maximum
    $left = ($size - ($dot + $px * ($Columns.Count - 1))) / 2
    $top = ($size - ($dot + $py * ($tallest - 1))) / 2
    $glow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 96, 165, 250))
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 199, 220, 255))
    for ($c = 0; $c -lt $Columns.Count; $c++) {
        $first = ($tallest - $Columns[$c]) / 2
        for ($k = 0; $k -lt $Columns[$c]; $k++) {
            $x = $left + $c * $px
            $y = $top + ($first + $k) * $py
            $e = $dot * 0.35
            $g.FillEllipse($glow, [single](($x - $e) * $S), [single](($y - $e) * $S), [single](($dot + 2 * $e) * $S), [single](($dot + 2 * $e) * $S))
            $g.FillEllipse($ink, [single]($x * $S), [single]($y * $S), [single]($dot * $S), [single]($dot * $S))
        }
    }
    $g.Dispose()

    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $h = [System.Drawing.Graphics]::FromImage($bmp)
    $h.InterpolationMode = 'HighQualityBicubic'; $h.PixelOffsetMode = 'HighQuality'
    $h.DrawImage($big, 0, 0, $size, $size)
    $h.Dispose(); $big.Dispose()
    return ConvertTo-Png $bmp
}

$master = Get-Master
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 256)
$images = foreach ($s in $sizes) { , (& { if ($Small.ContainsKey($s)) { New-Drawn $s } else { New-FromMaster $master $s } }) }
$master.Dispose()

# ICONDIR, then one ICONDIRENTRY per size (width/height 0 means 256), then the PNGs.
$file = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($png in $images) { $w.Write($png) }
$w.Flush()
[IO.File]::WriteAllBytes($out, $file.ToArray())
Write-Host "wrote $out ($($file.Length) bytes, sizes $($sizes -join ', '))"
