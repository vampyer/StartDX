<#
  Generates the 256x256 px (= 128 DIP @2x) tile overlay art referenced by Theme.Tile.ArtOverlay.
  Re-run after tweaking, then rebuild.  Usage:  powershell -File tools\generate-tile-art.ps1

  The overlays are *transparent* PNGs: they only carry light (specular, rim, bloom, grain), never the plate colour,
  so the same art works over any plate fill. Corner radii are baked in to match each theme's
  Theme.Tile.Plate.CornerRadius (Glass 18 DIP = 36 px, Dark 12 DIP = 24 px, Retro 0).
#>
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\StartDX.Dock\Assets\Tiles"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$S = 256

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0.5) { $p.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $w, $h))); return $p }
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Add-Grain($bmp, [int]$alpha, [int]$seed, $clip) {
    $rng = New-Object System.Random($seed)
    for ($y = 0; $y -lt $S; $y++) {
        for ($x = 0; $x -lt $S; $x++) {
            if (-not $clip.IsVisible($x, $y)) { continue }
            $n = $rng.Next(0, 100)
            if ($n -lt 38) {
                $c = $bmp.GetPixel($x, $y)
                $a = [Math]::Min(255, $c.A + $rng.Next(0, $alpha))
                $v = $rng.Next(200, 255)
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, $v, $v, $v))
            }
        }
    }
}

# ─────────────────────────── glass.png ───────────────────────────
$bmp, $g = New-Canvas
$r = 36
$clipPath = New-RoundedPath 0 0 $S $S $r
$g.SetClip($clipPath)

# Specular: light falling from the top-left, ending in a soft curved edge
$spec = New-Object System.Drawing.Drawing2D.GraphicsPath
$spec.AddEllipse(-90, -170, 470, 330)
$sb = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(180, 190)),
    [System.Drawing.Color]::FromArgb(88, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
$g.FillPath($sb, $spec)

# Bottom bloom (cool cyan)
$bloom = New-Object System.Drawing.Drawing2D.GraphicsPath
$bloom.AddEllipse(30, 170, 200, 130)
$pb = New-Object System.Drawing.Drawing2D.PathGradientBrush($bloom)
$pb.CenterColor = [System.Drawing.Color]::FromArgb(70, 90, 200, 255)
$pb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 90, 200, 255))
$g.FillPath($pb, $bloom)
$g.ResetClip()

# Rim light: bright top-left, faint bottom-right
$rim = New-RoundedPath 3 3 ($S - 6) ($S - 6) ($r - 3)
$rimBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($S, $S)),
    [System.Drawing.Color]::FromArgb(190, 255, 255, 255), [System.Drawing.Color]::FromArgb(60, 182, 232, 255))
$pen = New-Object System.Drawing.Pen($rimBrush, 2.5)
$g.DrawPath($pen, $rim)

Add-Grain $bmp 14 7 (New-Object System.Drawing.Region($clipPath))
$bmp.Save((Join-Path $out "glass.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# ─────────────────────────── dark.png ───────────────────────────
$bmp, $g = New-Canvas
$r = 24
$clipPath = New-RoundedPath 0 0 $S $S $r
$g.SetClip($clipPath)

# Vignette
$full = New-Object System.Drawing.Drawing2D.GraphicsPath
$full.AddRectangle((New-Object System.Drawing.RectangleF(-40, -40, ($S + 80), ($S + 80))))
$vig = New-Object System.Drawing.Drawing2D.PathGradientBrush($full)
$vig.CenterColor = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
$vig.SurroundColors = @([System.Drawing.Color]::FromArgb(120, 0, 0, 0))
$vig.FocusScales = New-Object System.Drawing.PointF(0.55, 0.55)
$g.FillPath($vig, $full)

# Fine diagonal weave
$weave = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(9, 255, 255, 255), 1)
for ($i = -$S; $i -lt $S * 2; $i += 6) { $g.DrawLine($weave, $i, 0, $i + $S, $S) }

# Cyan corner glint (bottom-left)
$glintPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$glintPath.AddEllipse(-60, 150, 190, 190)
$gb = New-Object System.Drawing.Drawing2D.PathGradientBrush($glintPath)
$gb.CenterColor = [System.Drawing.Color]::FromArgb(64, 0, 229, 255)
$gb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 0, 229, 255))
$g.FillPath($gb, $glintPath)
$g.ResetClip()

# Inner hairline rim
$rim = New-RoundedPath 3 3 ($S - 6) ($S - 6) ($r - 3)
$rimBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $S)),
    [System.Drawing.Color]::FromArgb(110, 120, 130, 160), [System.Drawing.Color]::FromArgb(30, 120, 130, 160))
$g.DrawPath((New-Object System.Drawing.Pen($rimBrush, 2)), $rim)

$bmp.Save((Join-Path $out "dark.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# ─────────────────────────── retro.png ───────────────────────────
$bmp, $g = New-Canvas
$g.SmoothingMode = 'None'      # keep bevel lines pixel-crisp
$g.PixelOffsetMode = 'None'

# Inner (second) bevel: light top/left, mid-gray bottom/right, inset from the outer slab bevel
$inset = 14; $t = 2
$hi = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 255, 255, 255))
$lo = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 128, 128, 128))
$g.FillRectangle($hi, $inset, $inset, $S - 2 * $inset, $t)                     # top
$g.FillRectangle($hi, $inset, $inset, $t, $S - 2 * $inset)                     # left
$g.FillRectangle($lo, $inset, $S - $inset - $t, $S - 2 * $inset, $t)          # bottom
$g.FillRectangle($lo, $S - $inset - $t, $inset, $t, $S - 2 * $inset)           # right

$bmp.Save((Join-Path $out "retro.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# ─────────────────────────── neon.png ───────────────────────────
$bmp, $g = New-Canvas
$r = 24
$clipPath = New-RoundedPath 0 0 $S $S $r
$g.SetClip($clipPath)

# Faint CRT scanlines
$scan = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(12, 57, 255, 20), 1)
for ($y = 0; $y -lt $S; $y += 4) { $g.DrawLine($scan, 0, $y, $S, $y) }

# Green bloom from the bottom-left corner and a cooler one from the top-right
foreach ($spot in @(@(-70, 140, 230, 90), @(140, -90, 200, 60))) {
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddEllipse($spot[0], $spot[1], $spot[2], $spot[2])
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush($gp)
    $pg.CenterColor = [System.Drawing.Color]::FromArgb($spot[3], 57, 255, 20)
    $pg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 57, 255, 20))
    $g.FillPath($pg, $gp)
}

# Edge vignette to keep the centre (the app icon) dark and legible
$full = New-Object System.Drawing.Drawing2D.GraphicsPath
$full.AddRectangle((New-Object System.Drawing.RectangleF(-40, -40, ($S + 80), ($S + 80))))
$vig = New-Object System.Drawing.Drawing2D.PathGradientBrush($full)
$vig.CenterColor = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
$vig.SurroundColors = @([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
$vig.FocusScales = New-Object System.Drawing.PointF(0.6, 0.6)
$g.FillPath($vig, $full)
$g.ResetClip()

# Neon inner rim: brighter along the top, fading toward the bottom
$rim = New-RoundedPath 3 3 ($S - 6) ($S - 6) ($r - 3)
$rimBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $S)),
    [System.Drawing.Color]::FromArgb(150, 57, 255, 20), [System.Drawing.Color]::FromArgb(30, 57, 255, 20))
$g.DrawPath((New-Object System.Drawing.Pen($rimBrush, 2)), $rim)

$bmp.Save((Join-Path $out "neon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Get-ChildItem $out -Filter *.png | Select-Object Name, Length
