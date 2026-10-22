# Generates the placeholder FADER icons (assets/fader.ico and assets/fader-locked.ico).
# No official logo was supplied, so this draws a simple mark: three fader tracks with one amber knob.
# Replace the .ico files with real artwork whenever one exists; nothing else needs to change.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\assets")
)

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(0.5, $r * 2)
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$s, [bool]$locked) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    if ($locked) {
        $plate = New-Color "#E8A33D"; $edge = New-Color "#B97B1B"; $track = New-Color "#6E4A0D"; $knob = New-Color "#1B1B1A"; $accent = New-Color "#1B1B1A"
    } else {
        $plate = New-Color "#262625"; $edge = New-Color "#66655F"; $track = New-Color "#77756E"; $knob = New-Color "#ECEBE7"; $accent = New-Color "#E8A33D"
    }

    $m = $s * 0.04
    $plateSize = $s - 2 * $m
    $path = New-RoundedPath $m $m $plateSize $plateSize ($s * 0.2)
    $g.FillPath((New-Object System.Drawing.SolidBrush $plate), $path)
    $pen = New-Object System.Drawing.Pen $edge, ([Math]::Max(1, $s * 0.035))
    $g.DrawPath($pen, $path)

    $trackW = [Math]::Max(1.5, $s * 0.06)
    $knobW = $s * 0.2
    $knobH = [Math]::Max(2.5, $s * 0.1)
    $top = $s * 0.2
    $bottom = $s * 0.8
    $xs = @(0.27, 0.5, 0.73)
    # Knob heights; when locked all three line up, which reads as "set and held".
    $ys = if ($locked) { @(0.5, 0.5, 0.5) } else { @(0.62, 0.34, 0.5) }

    for ($i = 0; $i -lt 3; $i++) {
        $cx = $s * $xs[$i]
        $trackPath = New-RoundedPath ($cx - $trackW / 2) $top $trackW ($bottom - $top) ($trackW / 2)
        $g.FillPath((New-Object System.Drawing.SolidBrush $track), $trackPath)
        $cy = $s * $ys[$i]
        $color = if ($i -eq 1) { $accent } else { $knob }
        $knobPath = New-RoundedPath ($cx - $knobW / 2) ($cy - $knobH / 2) $knobW $knobH ([Math]::Max(0.8, $s * 0.025))
        $g.FillPath((New-Object System.Drawing.SolidBrush $color), $knobPath)
    }

    $g.Dispose()
    return $bmp
}

function Write-Ico([string]$file, [bool]$locked) {
    $pngs = @()
    foreach ($s in $sizes) {
        $bmp = New-IconBitmap $s $locked
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , $ms.ToArray()
        $bmp.Dispose(); $ms.Dispose()
    }

    $fs = [System.IO.File]::Create($file)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Dispose(); $fs.Dispose()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Write-Ico (Join-Path $OutDir "fader.ico") $false
Write-Ico (Join-Path $OutDir "fader-locked.ico") $true
Write-Host "Wrote icons to $OutDir"
