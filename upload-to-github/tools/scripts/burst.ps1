param([int]$Seconds = 120, [double]$Every = 2.5, [string]$Dir = "$PSScriptRoot\burst")
# Takes a screenshot every few seconds, but only while Hollow Knight is the foreground window.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class F {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[F]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Force $Dir | Out-Null
$end = (Get-Date).AddSeconds($Seconds); $n = 0
while ((Get-Date) -lt $end) {
  $p = Get-Process hollow_knight -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($p -and $p.MainWindowHandle -ne 0 -and [F]::GetForegroundWindow() -eq $p.MainWindowHandle) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap 1920, 1080
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $small = New-Object System.Drawing.Bitmap $bmp, 960, 540
    $small.Save((Join-Path $Dir ("f{0:D3}.png" -f $n)), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $small.Dispose(); $n++
  }
  Start-Sleep -Milliseconds ([int]($Every * 1000))
}
"captured $n frames in $Dir"
