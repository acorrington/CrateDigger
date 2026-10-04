<#
.SYNOPSIS
    Generates the CrateDigger plugin thumbnail (flat, two-tone, MBBackup style).

.DESCRIPTION
    Emby's GET /Plugins/{Id}/Thumb serves an embedded resource named
    "{something}.thumb.png" from the plugin assembly (first-party plugins embed
    e.g. "MBBackup.thumb.png"). The exact lookup key (plugin Name vs type
    namespace vs assembly simple name) is version-dependent — and the ILRepack
    merge renames the assembly simple name — so this script emits one artwork
    file plus two alias copies covering all three conventions; the csproj embeds
    each under its expected LogicalName.

    Style matches first-party thumbs: 600x338, flat, single foreground color
    (#F2EDE4 cream) on a solid vivid background (#E8862B warm crate-orange),
    legible down to card size: a vinyl record rising out of a wooden crate.

.EXAMPLE
    powershell -File tools/make-thumb.ps1
#>
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$w = 600
$h = 338
$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/CrateDigger.Plugin/Resources'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$bg = [System.Drawing.Color]::FromArgb(232, 134, 43)   # #E8862B crate orange
$fg = [System.Drawing.Color]::FromArgb(242, 237, 228)   # #F2EDE4 cream

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

$bgBrush = New-Object System.Drawing.SolidBrush($bg)
$fgBrush = New-Object System.Drawing.SolidBrush($fg)
$penBg7 = New-Object System.Drawing.Pen($bg, 7)
$penBg5 = New-Object System.Drawing.Pen($bg, 5)

try {
    $g.Clear($bg)

    # ---- vinyl record (drawn first; the crate overlaps its lower half) ----
    $cx = 300; $cy = 138; $r = 98
    $g.FillEllipse($fgBrush, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $g.DrawEllipse($penBg7, $cx - 66, $cy - 66, 132, 132)   # groove ring
    $g.DrawEllipse($penBg5, $cx - 44, $cy - 44, 88, 88)     # inner groove
    $g.FillEllipse($bgBrush, $cx - 13, $cy - 13, 26, 26)     # spindle hole

    # ---- wooden crate (over the record's lower half) ----
    $crX = 165; $crY = 158; $crW = 270; $crH = 132; $crD = 18
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($crX, $crY, $crD * 2, $crD * 2, 180, 90)
    $path.AddArc($crX + $crW - $crD * 2, $crY, $crD * 2, $crD * 2, 270, 90)
    $path.AddArc($crX + $crW - $crD * 2, $crY + $crH - $crD * 2, $crD * 2, $crD * 2, 0, 90)
    $path.AddArc($crX, $crY + $crH - $crD * 2, $crD * 2, $crD * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($fgBrush, $path)

    # slat gaps (background-colored stripes inside the crate)
    foreach ($sy in 192, 226, 260) {
        $g.FillRectangle($bgBrush, ($crX + 14), $sy, ($crW - 28), 11)
    }

    $bmp.Save((Join-Path $outDir 'thumb.png'), [System.Drawing.Imaging.ImageFormat]::Png)

    # Alias copies for the alternate lookup conventions (see header).
    Copy-Item (Join-Path $outDir 'thumb.png') (Join-Path $outDir 'thumb-assembly.png') -Force
    Copy-Item (Join-Path $outDir 'thumb.png') (Join-Path $outDir 'thumb-merged.png') -Force

    Write-Host "wrote thumb.png + aliases ($w x $h) to $outDir"
}
finally {
    $g.Dispose()
    $bmp.Dispose()
    $bgBrush.Dispose()
    $fgBrush.Dispose()
    $penBg7.Dispose()
    $penBg5.Dispose()
}