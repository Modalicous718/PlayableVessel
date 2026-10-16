param([string]$Dir, [string[]]$Frames, [string]$Out, [int]$Cell = 360)
# For each frame, find the brightest-ish region is hard; instead crop a 320x180 window centred on (cx,cy) given as name:cx:cy.
Add-Type -AssemblyName System.Drawing
$n = $Frames.Count; $cols = [math]::Min(4, $n); $rows = [math]::Ceiling($n / $cols)
$W = $Cell * 16 / 9; $H = $Cell
$sheet = New-Object System.Drawing.Bitmap ([int]($W * $cols)), ([int]($H * $rows))
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode = 'HighQualityBicubic'
$font = New-Object System.Drawing.Font "Arial", 14, ([System.Drawing.FontStyle]::Bold)
$i = 0
foreach ($spec in $Frames) {
  $p = $spec.Split(':'); $img = [System.Drawing.Image]::FromFile((Join-Path $Dir ($p[0] + '.png')))
  $cx = [int]$p[1]; $cy = [int]$p[2]; $cw = 240; $ch = 135
  $src = New-Object System.Drawing.Rectangle ([math]::Max(0, $cx - $cw / 2)), ([math]::Max(0, $cy - $ch / 2)), $cw, $ch
  $x = ($i % $cols) * $W; $y = [math]::Floor($i / $cols) * $H
  $g.DrawImage($img, (New-Object System.Drawing.Rectangle ([int]$x), ([int]$y), ([int]$W), ([int]$H)), $src, 'Pixel')
  $g.FillRectangle([System.Drawing.Brushes]::Black, $x, $y, 60, 22); $g.DrawString($p[0], $font, [System.Drawing.Brushes]::Yellow, $x + 2, $y + 1)
  $img.Dispose(); $i++
}
$sheet.Save($Out); "ok"
