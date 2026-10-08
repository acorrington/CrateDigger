<#
.SYNOPSIS
    Regenerates the CrateDigger plugin thumbnail to match the Trawler/Reel family palette.

.DESCRIPTION
    Sibling-plugin identity (sampled from D:\Trawler\thumb.jpg and D:\Reel\thumb.jpg):
      background #181E38 (navy) · cream #F2ECE0 · orange accent #E8862B
      uppercase wordmark + tagline with an orange underline rule.

    CrateDigger keeps its crate+vinyl motif, recolored to that palette, with the
    "CrateDigger" wordmark and an "AI PLAYLISTS" tagline. 600x338 (matches Reel /
    our deployed PNG). Writes Resources/thumb.png (+ two alias copies consumed by the
    csproj's three LogicalName entries for the plugins-page Thumb endpoint).

    ASCII-only header (Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI).

.EXAMPLE
    powershell -File tools\make-thumb.ps1
#>
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$w = 600
$h = 338
$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/CrateDigger.Plugin/Resources'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$bg    = [System.Drawing.Color]::FromArgb(24, 30, 56)     # #181E38 navy
$cream = [System.Drawing.Color]::FromArgb(242, 236, 224)  # #F2ECE0
$orange= [System.Drawing.Color]::FromArgb(232, 134, 43)   # #E8862B

$bgBrush    = New-Object System.Drawing.SolidBrush($bg)
$creamBrush = New-Object System.Drawing.SolidBrush($cream)
$orangeBrush= New-Object System.Drawing.SolidBrush($orange)
$penBg7 = New-Object System.Drawing.Pen($bg, 7)
$penBg5 = New-Object System.Drawing.Pen($bg, 5)
$penCream3 = New-Object System.Drawing.Pen($cream, 3)

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

try {
    $g.Clear($bg)

    # ---- Left: crate + vinyl motif (cream on navy, orange grooves) ----
    $cx = 150; $cy = 150; $r = 92
    # vinyl disc (cream)
    $g.FillEllipse($creamBrush, $cx - $r, $cy - $r, 2*$r, 2*$r)
    # grooves (navy rings)
    $g.DrawEllipse($penBg7, $cx - 64, $cy - 64, 128, 128)
    $g.DrawEllipse($penBg5, $cx - 42, $cy - 42, 84, 84)
    # center hole (navy)
    $g.FillEllipse($bgBrush, $cx - 13, $cy - 13, 26, 26)
    # orange label dot behind hole for accent
    $g.FillEllipse($orangeBrush, $cx - 26, $cy - 26, 52, 52)
    $g.FillEllipse($bgBrush, $cx - 12, $cy - 12, 24, 24)

    # crate (cream box, orange slats) overlapping lower-right of disc
    $crX = 118; $crY = 168; $crW = 230; $crH = 120; $crD = 16
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($crX, $crY, $crD*2, $crD*2, 180, 90)
    $path.AddArc($crX + $crW - $crD*2, $crY, $crD*2, $crD*2, 270, 90)
    $path.AddArc($crX + $crW - $crD*2, $crY + $crH - $crD*2, $crD*2, $crD*2, 0, 90)
    $path.AddArc($crX, $crY + $crH - $crD*2, $crD*2, $crD*2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($creamBrush, $path)
    # orange slat gaps
    foreach ($sy in 196, 232) {
        $g.FillRectangle($orangeBrush, ($crX + 16), $sy, ($crW - 32), 12)
    }

    # ---- Right: wordmark + tagline (kept clear of the crate motif) ----
    $rightX = 350
    $availW = $w - $rightX - 16

    # Pick the largest font size that fits "CrateDigger" in the available width.
    $title = 'CrateDigger'
    $titleFont = $null
    for ($sz = 44; $sz -ge 24; $sz -= 2) {
        $trial = New-Object System.Drawing.Font('Segoe UI', $sz, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        if ($g.MeasureString($title, $trial).Width -le $availW) { $titleFont = $trial; break }
        $trial.Dispose()
    }
    if (-not $titleFont) { $titleFont = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel) }
    $tagFont = New-Object System.Drawing.Font('Segoe UI', 17, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)

    $measureT = $g.MeasureString($title, $titleFont)
    $titleY = 130
    $g.DrawString($title, $titleFont, $creamBrush, (New-Object System.Drawing.RectangleF($rightX, $titleY, $availW, 58)))

    # tagline + orange underline (Reel/Trawler convention)
    $tag = 'AI PLAYLISTS'
    $tagY = $titleY + [int]$measureT.Height + 6
    $measureTag = $g.MeasureString($tag, $tagFont)
    $g.DrawString($tag, $tagFont, $orangeBrush, (New-Object System.Drawing.RectangleF($rightX, $tagY, $availW, 30)))

    $underlineY = [int]($tagY + $measureTag.Height + 3)
    $g.FillRectangle($orangeBrush, $rightX, $underlineY, [int]$measureTag.Width, 5)
    Write-Host ("title font: {0}px, width {1:N0}/{2}" -f $titleFont.Size, $measureT.Width, $availW)

    $bmp.Save((Join-Path $outDir 'thumb.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    # Alias copies for the three LogicalName entries (namespace / assembly / merged-name).
    Copy-Item (Join-Path $outDir 'thumb.png') (Join-Path $outDir 'thumb-assembly.png') -Force
    Copy-Item (Join-Path $outDir 'thumb.png') (Join-Path $outDir 'thumb-merged.png') -Force
    Write-Host "wrote thumb.png + aliases ($w x $h) to $outDir"
}
finally {
    $g.Dispose()
    $bmp.Dispose()
    $bgBrush.Dispose(); $creamBrush.Dispose(); $orangeBrush.Dispose()
    $penBg7.Dispose(); $penBg5.Dispose(); $penCream3.Dispose()
    $titleFont.Dispose(); $tagFont.Dispose()
}