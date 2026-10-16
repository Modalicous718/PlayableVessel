param([string]$Dir, [int]$Start = 0, [int]$Count = 12, [int]$Cols = 3, [int]$W = 640, [int]$H = 360, [string]$Out)
# Contact sheet of numbered frames (f000.png...), labelled with their index.
Add-Type -AssemblyName System.Drawing
$files = Get-ChildItem $Dir -Filter "f*.png" | Sort-Object Name | Select-Object -Skip $Start -First $Count
$rows = [math]::Ceiling($files.Count / $Cols)
$sheet = New-Object System.Drawing.Bitmap ($W * $Cols), ($H * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$font = New-Object System.Drawing.Font "Arial", 16, ([System.Drawing.FontStyle]::Bold)
$i = 0
foreach ($f in $files) {
  $img = [System.Drawing.Image]::FromFile($f.FullName)
  $x = ($i % $Cols) * $W; $y = [math]::Floor($i / $Cols) * $H
  $g.DrawImage($img, $x, $y, $W, $H)
  $g.FillRectangle([System.Drawing.Brushes]::Black, $x, $y, 70, 26)
  $g.DrawString($f.BaseName, $font, [System.Drawing.Brushes]::Yellow, $x + 2, $y + 2)
  $img.Dispose(); $i++
}
$sheet.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"sheet $Out ($i frames)"
