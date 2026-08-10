Add-Type -AssemblyName System.Drawing
$dir = "C:\Users\senke\Desktop\VoicePrompt\assets"
New-Item -ItemType Directory -Path $dir -Force | Out-Null

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$bmp.SetResolution(96, 96)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)

function RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$rect = RoundedPath 8 8 240 240 56
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 8)), (New-Object System.Drawing.Point(0, 248)),
    [System.Drawing.Color]::FromArgb(255, 99, 102, 241),
    [System.Drawing.Color]::FromArgb(255, 139, 92, 246))
$g.FillPath($grad, $rect)

function ArcPen([int]$width) {
    $p = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 255), $width)
    $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    return $p
}

$micPen = ArcPen 22
$g.DrawArc($micPen, 93, 46, 70, 70, 180, 180)
$g.DrawArc($micPen, 93, 130, 70, 70, 0, 180)

$footPen = ArcPen 20
$g.DrawEllipse($footPen, 56, 190, 144, 34)

$body = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$g.FillEllipse($body, 88, 76, 80, 120)

$inner = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 99, 102, 241))
$g.FillEllipse($inner, 108, 96, 40, 60)

$wavePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 255, 255, 255), 12)
$wavePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$wavePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($wavePen, 172, 96, 66, 66, -60, 120)
$g.DrawArc($wavePen, 182, 110, 36, 36, 20, 100)

$g.Dispose()
$png = Join-Path $dir "logo.png"
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "saved $png"

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()
$ms.Dispose()
$bmp.Dispose()

$ico = Join-Path $dir "icon.ico"
$fs = New-Object System.IO.FileStream($ico, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
$bw.Write([Byte]0); $bw.Write([Byte]0)
$bw.Write([Byte]0); $bw.Write([Byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32)
$bw.Write([UInt32]$pngBytes.Length)
$bw.Write([UInt32]22)
$bw.Write($pngBytes)
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output "saved $ico ($($pngBytes.Length) bytes)"

$sh = New-Object -ComObject WScript.Shell
$startup = [Environment]::GetFolderPath("Startup")
$desktop = [Environment]::GetFolderPath("Desktop")
@("$startup\Voice Typing (faster-whisper-dictation).lnk",
  "$desktop\Start Voice Typing.lnk",
  "$desktop\Stop Voice Typing.lnk") | ForEach-Object {
    $lnk = $sh.CreateShortcut($_)
    $lnk.IconLocation = "$ico,0"
    $lnk.Save()
    Write-Output "icon set on $(Split-Path $_ -Leaf)"
}