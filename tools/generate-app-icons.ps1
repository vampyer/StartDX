<#
  Generates the application icons:
      src\StartDX.Dock\Assets\Icons\startdx.ico              (dock: the 2x2 Start grid above a dock bar)
      src\StartDX.Settings\Assets\startdx-settings.ico       (settings: a gear)
      docs\icons\*.png                                        (256 px previews)

  Both share one visual family: an obsidian rounded tile with a glassy highlight and a cyan -> neon-green rim, and cyan -> neon-green
  artwork with a soft glow. Every frame (16-256 px) is drawn natively, not scaled down: frames <= 48 px drop the small details
  (dock bar, glow) and use bolder shapes so the icon stays readable in title bars and the tray.

  Usage:  powershell -File tools\generate-app-icons.ps1
#>
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$outputs = @{
    dock     = Join-Path $root "src\StartDX.Dock\Assets\Icons\startdx.ico"
    settings = Join-Path $root "src\StartDX.Settings\Assets\startdx-settings.ico"
}
$previewDir = Join-Path $root "docs\icons"
foreach ($p in @($outputs.dock, $outputs.settings, (Join-Path $previewDir "x"))) { New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null }

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

function Argb([int]$a, [string]$hex) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B)
}

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float](2 * $r)
    $p.AddArc([float]$x, [float]$y, $d, $d, 180, 90)
    $p.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-GearPath([double]$cx, [double]$cy, [double]$rTip, [double]$rRoot, [double]$rHole, [int]$teeth) {
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $period = 360.0 / $teeth
    $dRoot = $period * 0.5 * 0.42       # half-width of a tooth at its base
    $dTip = $period * 0.5 * 0.24        # half-width at the tip (teeth taper)
    function Polar([double]$r, [double]$deg) {
        $rad = ($deg - 90) * [Math]::PI / 180.0
        New-Object System.Drawing.PointF([float]($cx + $r * [Math]::Cos($rad)), [float]($cy + $r * [Math]::Sin($rad)))
    }
    for ($i = 0; $i -lt $teeth; $i++) {
        $a = $i * $period
        $pts.Add((Polar $rRoot ($a - $dRoot))); $pts.Add((Polar $rTip ($a - $dTip)))
        $pts.Add((Polar $rTip ($a + $dTip)));   $pts.Add((Polar $rRoot ($a + $dRoot)))
        $span = $period - 2 * $dRoot
        for ($k = 1; $k -le 3; $k++) { $pts.Add((Polar $rRoot ($a + $dRoot + $span * $k / 4.0))) }   # root arc between teeth
    }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath([System.Drawing.Drawing2D.FillMode]::Alternate)
    $path.AddPolygon($pts.ToArray())
    $path.AddEllipse([float]($cx - $rHole), [float]($cy - $rHole), [float](2 * $rHole), [float](2 * $rHole))   # even-odd => a hole
    $path
}

function Render-Icon([string]$kind, [int]$size) {
    $ss = if ($size -le 48) { 8 } else { 4 }            # supersampling factor
    $S = $size * $ss
    $detail = $size -ge 64
    $big = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # ── Base tile ─────────────────────────────────────────────────────────────────────
    $m = 0.03
    $bg = New-RoundedPath ($m * $S) ($m * $S) ((1 - 2 * $m) * $S) ((1 - 2 * $m) * $S) (0.22 * $S)
    $g.FillPath((New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($S, $S)), (Argb 255 "#232C40"), (Argb 255 "#04060A"))), $bg)

    $g.SetClip($bg)
    $specPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $specPath.AddEllipse([float](-0.35 * $S), [float](-0.62 * $S), [float](1.7 * $S), [float](1.2 * $S))
    # The gradient must end exactly where the ellipse ends (y = 0.58): gradient brushes TILE past their end point, which would
    # restart the highlight as a visible grey band just below it.
    $specBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, [float](0.58 * $S))), (Argb 52 "#FFFFFF"), (Argb 0 "#FFFFFF"))
    $g.FillPath($specBrush, $specPath)
    $g.ResetClip()

    $rimW = [Math]::Max(1.0, 0.014 * $S)
    $rim = New-RoundedPath (($m + 0.008) * $S) (($m + 0.008) * $S) ((1 - 2 * ($m + 0.008)) * $S) ((1 - 2 * ($m + 0.008)) * $S) (0.212 * $S)
    $g.DrawPath((New-Object System.Drawing.Pen((New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($S, $S)), (Argb 190 "#00E5FF"), (Argb 150 "#39FF14"))), [float]$rimW)), $rim)

    # ── Artwork ───────────────────────────────────────────────────────────────────────
    $cyan = "#00E5FF"; $green = "#39FF14"
    if ($kind -eq "dock") {
        if ($detail) { $tile = 0.26; $gap = 0.05; $y0 = 0.14 } else { $tile = 0.31; $gap = 0.06; $y0 = 0.16 }
        $gridW = 2 * $tile + $gap
        $x0 = (1 - $gridW) / 2
        $gridRect = New-Object System.Drawing.RectangleF([float]($x0 * $S), [float]($y0 * $S), [float]($gridW * $S), [float]($gridW * $S))
        $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush($gridRect, (Argb 255 $cyan), (Argb 255 $green), [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        $dim = 0, 34, 34, 76
        $idx = 0
        foreach ($r in 0, 1) { foreach ($c in 0, 1) {
            $tx = ($x0 + $c * ($tile + $gap)) * $S; $ty = ($y0 + $r * ($tile + $gap)) * $S; $tw = $tile * $S
            if ($detail) { for ($k = 5; $k -ge 1; $k--) {          # soft neon glow
                $e = $k * 0.006 * $S
                $g.FillPath((New-Object System.Drawing.SolidBrush((Argb (11 + 2 * (6 - $k)) $cyan))), (New-RoundedPath ($tx - $e) ($ty - $e) ($tw + 2 * $e) ($tw + 2 * $e) (0.075 * $S + $e)))
            } }
            $tp = New-RoundedPath $tx $ty $tw $tw (0.07 * $S)
            $g.FillPath($fill, $tp)
            $g.SetClip($tp)                                           # glassy top highlight
            $g.FillRectangle((New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, [float]$ty)), (New-Object System.Drawing.PointF(0, [float]($ty + $tw * 0.6))), (Argb 92 "#FFFFFF"), (Argb 0 "#FFFFFF"))), [float]$tx, [float]$ty, [float]$tw, [float]($tw * 0.6))
            if ($dim[$idx] -gt 0) { $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($dim[$idx], 0, 0, 0))), $tp) }
            $g.ResetClip()
            $idx++
        } }
        if ($detail) {                                                # dock bar with an active indicator and two idle ones
            $by = 0.805; $bh = 0.072
            $g.FillPath((New-Object System.Drawing.SolidBrush((Argb 40 "#FFFFFF"))), (New-RoundedPath ($x0 * $S) ($by * $S) ($gridW * $S) ($bh * $S) ($bh * $S / 2)))
            $pw = 0.12; $ph = 0.03; $py = $by + ($bh - $ph) / 2; $startX = $x0 + ($gridW - (3 * $pw + 2 * 0.05)) / 2
            foreach ($i in 0, 1, 2) {
                $px = ($startX + $i * ($pw + 0.05)) * $S
                $brush = if ($i -eq 0) { New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF([float]$px, 0)), (New-Object System.Drawing.PointF([float]($px + $pw * $S), 0)), (Argb 255 $cyan), (Argb 255 $green)) } else { New-Object System.Drawing.SolidBrush((Argb 110 "#FFFFFF")) }
                $g.FillPath($brush, (New-RoundedPath $px ($py * $S) ($pw * $S) ($ph * $S) ($ph * $S / 2)))
            }
        }
    }
    else {   # settings: gear
        $teeth = if ($size -le 24) { 6 } else { 8 }
        if ($size -le 32) { $rTip = 0.38; $rRoot = 0.29; $rHole = 0.14 } else { $rTip = 0.35; $rRoot = 0.275; $rHole = 0.125 }
        $gear = New-GearPath 0.5 0.5 ($rTip * $S) ($rRoot * $S) ($rHole * $S) $teeth
        $gear2 = New-GearPath ($S / 2) ($S / 2) ($rTip * $S) ($rRoot * $S) ($rHole * $S) $teeth
        $gb = $gear2.GetBounds()
        $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush($gb, (Argb 255 $cyan), (Argb 255 $green), [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        if ($detail) { for ($k = 5; $k -ge 1; $k--) {
            $pen = New-Object System.Drawing.Pen((Argb (9 + 2 * (6 - $k)) $cyan), [float]($k * 0.012 * $S)); $pen.LineJoin = 'Round'
            $g.DrawPath($pen, $gear2)
        } }
        $g.FillPath($fill, $gear2)
        $g.SetClip($gear2)
        $g.FillRectangle((New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, [float]$gb.Top)), (New-Object System.Drawing.PointF(0, [float]($gb.Top + $gb.Height * 0.55))), (Argb 96 "#FFFFFF"), (Argb 0 "#FFFFFF"))), $gb.Left, $gb.Top, $gb.Width, [float]($gb.Height * 0.55))
        $g.ResetClip()
    }
    $g.Dispose()

    # ── Down-sample to the final size ────────────────────────────────────────────────
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($bmp)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.SmoothingMode = 'HighQuality'; $g2.PixelOffsetMode = 'HighQuality'; $g2.CompositingQuality = 'HighQuality'
    $ia = New-Object System.Drawing.Imaging.ImageAttributes; $ia.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $g2.DrawImage($big, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)), 0, 0, $S, $S, [System.Drawing.GraphicsUnit]::Pixel, $ia)
    $g2.Dispose(); $big.Dispose()
    $bmp
}

function Write-Ico([string]$path, [System.Collections.Generic.List[object]]$frames) {
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    $blobs = @()
    foreach ($f in $frames) {
        $png = New-Object System.IO.MemoryStream
        $f.Bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $png.ToArray(); $blobs += , $bytes
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($b in $blobs) { $w.Write($b) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
}

foreach ($kind in "dock", "settings") {
    $frames = New-Object 'System.Collections.Generic.List[object]'
    foreach ($sz in $sizes) { $frames.Add([pscustomobject]@{ Size = $sz; Bitmap = (Render-Icon $kind $sz) }) }
    Write-Ico $outputs[$kind] $frames
    ($frames | Where-Object Size -eq 256).Bitmap.Save((Join-Path $previewDir "startdx-$kind.png"), [System.Drawing.Imaging.ImageFormat]::Png)

    # contact sheet (not part of the repo output): 256 | 128 | 64 | 48 | 32 | 24 | 16, small sizes also magnified 4x with nearest-neighbour
    $sheet = New-Object System.Drawing.Bitmap(900, 350)
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    $sg.Clear([System.Drawing.ColorTranslator]::FromHtml("#3B3F48"))
    $x = 10
    foreach ($sz in 256, 128, 64, 48) { $sg.DrawImage(($frames | Where-Object Size -eq $sz).Bitmap, $x, 10, $sz, $sz); $x += $sz + 10 }
    $sg.InterpolationMode = 'NearestNeighbor'; $sg.PixelOffsetMode = 'Half'
    $x = 10
    foreach ($sz in 32, 24, 16) {
        $bmp = ($frames | Where-Object Size -eq $sz).Bitmap
        $sg.DrawImage($bmp, $x, 280, $sz, $sz)                 # 1:1
        $sg.DrawImage($bmp, $x + $sz + 8, 280, $sz * 2, $sz * 2)   # 2x
        $x += $sz * 3 + 30
    }
    $sg.Dispose()
    $sheet.Save((Join-Path $previewDir "sheet-$kind.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $frames | ForEach-Object { $_.Bitmap.Dispose() }
    "{0,-9} -> {1}  ({2:N1} KB, {3} frames)" -f $kind, $outputs[$kind], ((Get-Item $outputs[$kind]).Length / 1KB), $sizes.Count
}
