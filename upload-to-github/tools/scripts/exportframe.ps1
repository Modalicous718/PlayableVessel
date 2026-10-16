param([string]$Dump, [string]$Prefix, [string]$Clip, [int]$Frame, [string]$Out)
# Saves one animation frame at native atlas resolution (upright), and prints its sprite-space info.
Add-Type -AssemblyName System.Drawing
$inv = [Globalization.CultureInfo]::InvariantCulture
function N($s) { [double]::Parse($s, $inv) }
$f = Import-Csv (Join-Path $Dump "$($Prefix)_clips.tsv") -Delimiter "`t" | Where-Object { $_.clip -eq $Clip -and [int]$_.index -eq $Frame } | Select-Object -First 1
$d = Import-Csv (Join-Path $Dump "$($Prefix)_sprites.tsv") -Delimiter "`t" | Where-Object { $_.collection -eq $f.collection -and $_.id -eq $f.id } | Select-Object -First 1
$atlas = [System.Drawing.Bitmap]::FromFile((Join-Path $Dump $d.atlas))
$W = [int]$d.w; $H = [int]$d.h
$sx = (N $d.umin) * $W; $sy = (1 - (N $d.vmax)) * $H
$sw = ((N $d.umax) - (N $d.umin)) * $W; $sh = ((N $d.vmax) - (N $d.vmin)) * $H
$crop = $atlas.Clone((New-Object System.Drawing.RectangleF $sx, $sy, $sw, $sh), $atlas.PixelFormat)
if ($d.flipped -ne 'None') { $crop.RotateFlip('Rotate90FlipY') }
$crop.Save($Out)
"$($d.name) id=$($d.id) flipped=$($d.flipped) px=$($crop.Width)x$($crop.Height) pos=($($d.posMinX),$($d.posMinY))..($($d.posMaxX),$($d.posMaxY))"
