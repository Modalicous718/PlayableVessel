param([string]$Dump, [string]$Prefix, [string[]]$Clips, [string]$Out, [int]$CellH = 220, [switch]$RotateCCW)
# Draws each requested clip as a row of frames, cut from the dumped atlases and placed by their
# sprite-space positions so the pose and movement read correctly.
Add-Type -AssemblyName System.Drawing
$sprites = @{}
Import-Csv (Join-Path $Dump "$($Prefix)_sprites.tsv") -Delimiter "`t" | ForEach-Object { $sprites["$($_.collection)|$($_.id)"] = $_ }
$frames = Import-Csv (Join-Path $Dump "$($Prefix)_clips.tsv") -Delimiter "`t"
$atlases = @{}
function Atlas($f) { if (-not $atlases[$f]) { $atlases[$f] = [System.Drawing.Bitmap]::FromFile((Join-Path $Dump $f)) }; $atlases[$f] }
$inv = [Globalization.CultureInfo]::InvariantCulture
function N($s) { [double]::Parse($s, $inv) }

$rows = @()
foreach ($c in $Clips) {
  $fs = @($frames | Where-Object { $_.clip -eq $c } | Sort-Object { [int]$_.index })
  if ($fs.Count -eq 0) { continue }
  $defs = @($fs | ForEach-Object { $sprites["$($_.collection)|$($_.id)"] } | Where-Object { $_ })
  $minX = ($defs | ForEach-Object { N $_.posMinX } | Measure-Object -Minimum).Minimum
  $maxX = ($defs | ForEach-Object { N $_.posMaxX } | Measure-Object -Maximum).Maximum
  $minY = ($defs | ForEach-Object { N $_.posMinY } | Measure-Object -Minimum).Minimum
  $maxY = ($defs | ForEach-Object { N $_.posMaxY } | Measure-Object -Maximum).Maximum
  $rows += ,@($c, $defs, $minX, $maxX, $minY, $maxY)
}
$scaleAll = 0
foreach ($r in $rows) { $s = $CellH / [math]::Max(0.01, ($r[5] - $r[4])); if ($scaleAll -eq 0 -or $s -lt $scaleAll) { $scaleAll = $s } }
$maxW = 0
foreach ($r in $rows) { $w = ($r[3] - $r[2]) * $scaleAll * $r[1].Count; if ($w -gt $maxW) { $maxW = $w } }
$labelW = 170
$bmp = New-Object System.Drawing.Bitmap ([int]($maxW + $labelW + 10)), ([int](($CellH + 10) * $rows.Count))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 60, 70, 90))
$font = New-Object System.Drawing.Font "Arial", 12, ([System.Drawing.FontStyle]::Bold)
$y0 = 0
foreach ($r in $rows) {
  $g.DrawString($r[0], $font, [System.Drawing.Brushes]::Yellow, 4, $y0 + 4)
  $cellW = ($r[3] - $r[2]) * $scaleAll; $x0 = $labelW; $i = 0
  foreach ($d in $r[1]) {
    $atlas = Atlas $d.atlas; $W = [int]$d.w; $H = [int]$d.h
    $sx = (N $d.umin) * $W; $sy = (1 - (N $d.vmax)) * $H
    $sw = ((N $d.umax) - (N $d.umin)) * $W; $sh = ((N $d.vmax) - (N $d.vmin)) * $H
    $crop = $atlas.Clone((New-Object System.Drawing.RectangleF $sx, $sy, $sw, $sh), $atlas.PixelFormat)
    if ($d.flipped -ne 'None') { if ($RotateCCW) { $crop.RotateFlip('Rotate90FlipNone') } else { $crop.RotateFlip('Rotate90FlipY') } }
    $dx = $x0 + ((N $d.posMinX) - $r[2]) * $scaleAll
    $dy = $y0 + ($r[5] - (N $d.posMaxY)) * $scaleAll
    $dw = ((N $d.posMaxX) - (N $d.posMinX)) * $scaleAll; $dh = ((N $d.posMaxY) - (N $d.posMinY)) * $scaleAll
    $g.DrawImage($crop, [single]$dx, [single]$dy, [single]$dw, [single]$dh)
    $g.DrawRectangle([System.Drawing.Pens]::DimGray, [single]$x0, [single]$y0, [single]$cellW, [single]$CellH)
    $g.DrawString("$i", $font, [System.Drawing.Brushes]::White, [single]($x0 + 2), [single]($y0 + $CellH - 18))
    $crop.Dispose(); $x0 += $cellW; $i++
  }
  $y0 += $CellH + 10
}
$bmp.Save($Out); "saved $Out ($($rows.Count) clips)"
